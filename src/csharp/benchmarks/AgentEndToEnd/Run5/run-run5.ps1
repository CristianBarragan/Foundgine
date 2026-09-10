[CmdletBinding()]
param(
    [string]$CustomerCounts = '10,100,1000,10000',
    [string]$Concurrency = '8,16,32,64',
    [string]$RunsPerTier = '30,30,30,30',
    [int]$Warmups = 5,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
# Docker Compose writes routine lifecycle messages to stderr. Do not promote
# those messages to PowerShell terminating errors; use LASTEXITCODE instead.
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '../../..')
$ComposeFile = Join-Path $PSScriptRoot 'docker-compose.yml'
$Project = 'foundgine-run5'
$DbProject = Join-Path $PSScriptRoot 'Database/Database.csproj'
$Report = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $Report | Out-Null
# Guard every input before splitting: an empty/whitespace string still
# produces a one-element array from -split (e.g. @('')), which would then
# fail [int] casting with a confusing error rather than a clear one here.
if ([string]::IsNullOrWhiteSpace($CustomerCounts)) { throw 'CustomerCounts must not be empty.' }
if ([string]::IsNullOrWhiteSpace($Concurrency)) { throw 'Concurrency must not be empty.' }
if ([string]::IsNullOrWhiteSpace($RunsPerTier)) { throw 'RunsPerTier must not be empty.' }

$customerCountsArray = @($CustomerCounts -split ',' | ForEach-Object { [int]$_.Trim() })
$concurrencyArray = @($Concurrency -split ',' | ForEach-Object { [int]$_.Trim() })
# NOTE: this cannot be named $runsPerTier. PowerShell variables are
# case-insensitive, so $runsPerTier and the [string]$RunsPerTier parameter
# above are literally the same variable slot. That slot carries the
# parameter's declared [string] type constraint for the rest of the
# script's scope, so assigning an int[] to it doesn't produce an array -
# PowerShell silently converts the array back to a (space-joined) string
# to satisfy the constraint. Every later ".Count" access on it then hits a
# System.String under Set-StrictMode and throws "The property 'Count'
# cannot be found on this object" - which is exactly what happened here,
# on every invocation regardless of tier count. Using a distinct name
# avoids colliding with the typed parameter.
$runsPerTierArray = @($RunsPerTier -split ',' | ForEach-Object { [int]$_.Trim() })

# @() always yields a real array, but guard explicitly anyway so a future
# refactor that drops the @() wrapper fails with a clear message here
# instead of a bare "'Count' cannot be found" further down the script.
if ($null -eq $customerCountsArray -or $null -eq $runsPerTierArray -or $null -eq $concurrencyArray) {
    throw 'Failed to parse CustomerCounts/Concurrency/RunsPerTier into arrays.'
}
if ($customerCountsArray.Count -ne $runsPerTierArray.Count) {
    throw 'CustomerCounts and RunsPerTier must have the same number of entries.'
}
# Any single customer-tier / single-concurrency override (e.g. -Smoke's 1/1,
# or an ad-hoc lightweight run like -CustomerCounts 1 -Concurrency 8) is a
# deliberate one-off and is exempt from the fixed tier matrix below, which
# otherwise governs real (multi-tier) benchmark runs. Previously this only
# exempted the literal 1-customer/concurrency-1 -Smoke shape, so any other
# single-tier override incorrectly fell through to the full-matrix
# assertion below and failed.
$isSingleTierOverride = ($customerCountsArray.Count -eq 1 -and $concurrencyArray.Count -eq 1)
if (-not $isSingleTierOverride) {
    if ($customerCountsArray.Count -ne 4 -or ($customerCountsArray -join ',') -ne '10,100,1000,10000') {
        throw 'Run 5 (multi-tier) requires the same customer tiers as Run 2: 10,100,1000,10000.'
    }
    if (($concurrencyArray -join ',') -ne '8,16,32,64') {
        throw 'Run 5 (multi-tier) requires the same concurrency tiers as Run 2: 8,16,32,64.'
    }
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return $listener.LocalEndpoint.Port
    }
    finally {
        $listener.Stop()
    }
}

function Compose([string[]]$ComposeArgs) {
    & docker compose -p $Project -f $ComposeFile @ComposeArgs
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose failed: $($ComposeArgs -join ' ')"
    }
}

function Wait-Http([string]$Url, [int]$TimeoutSeconds = 180) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500) {
                return
            }
        }
        catch {}
        Start-Sleep -Seconds 2
    }
    Compose @('ps')
    throw "Endpoint did not become ready: $Url"
}

function Wait-Tcp([string]$HostName, [int]$Port, [int]$TimeoutSeconds = 60) {
    # The docker-exec pg_isready loop below only proves Postgres is ready
    # *inside* the container. It doesn't prove the host-published port
    # ($HostName:$Port, the exact endpoint the .NET seeder's Npgsql
    # connection uses) is accepting external connections yet - there can be
    # a short lag between "container reports healthy" and the host-side
    # port-forward accepting traffic (see the matching fix in
    # Run4/run-run4.ps1, which hit this as an Npgsql connect timeout).
    # Probe the real external endpoint directly before handing off to dotnet.
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $client = [System.Net.Sockets.TcpClient]::new()
            try {
                $connectTask = $client.ConnectAsync($HostName, $Port)
                if ($connectTask.Wait(2000) -and $client.Connected) { return }
            } finally { $client.Dispose() }
        } catch {}
        Start-Sleep -Milliseconds 500
    }
    throw "Postgres host port did not accept connections: ${HostName}:${Port}"
}

try {
    Compose @('down','-v','--remove-orphans')
    Compose @('build','mcp-efcore','mcp-foundgine')

    for ($index = 0; $index -lt $customerCountsArray.Count; $index++) {
        $customerCount = $customerCountsArray[$index]
        $runsForTier = $runsPerTierArray[$index]
        $tier = '{0:D5}-customers' -f $customerCount
        $tierRoot = Join-Path $Report $tier
        New-Item -ItemType Directory -Force -Path $tierRoot | Out-Null

        Write-Host ''
        Write-Host '============================================'
        Write-Host " Run 5 fixture tier: $customerCount customers"
        Write-Host '============================================'

        # Fresh PostgreSQL database for every customer tier.
        Compose @('down','-v','--remove-orphans','--timeout','30')

        $postgresHostPort = Get-FreeTcpPort
        $env:POSTGRES_HOST_PORT = $postgresHostPort.ToString()
        $connectionString = "Host=localhost;Port=$postgresHostPort;Database=foundgine_benchmark;Username=benchmark;Password=benchmark"
        $env:BankingConnectionString = $connectionString
        $env:TRANSFER_FUNDS_CUSTOMERS = $customerCount.ToString()

        Write-Host "Run 5 PostgreSQL host port: $postgresHostPort"
        Compose @('up','-d','--wait','postgres')

        # Explicit readiness check, matching the Run 4 fixture lifecycle.
        $dbReady = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            $containerId = (& docker compose -p $Project -f $ComposeFile ps -q postgres 2>$null).Trim()
            if ($containerId) {
                & docker exec $containerId pg_isready -U benchmark -d foundgine_benchmark 2>$null | Out-Null
                if ($LASTEXITCODE -eq 0) {
                    $dbReady = $true
                    break
                }
            }
            Start-Sleep -Seconds 1
        }
        if (-not $dbReady) {
            Compose @('ps')
            Compose @('logs','postgres')
            throw "PostgreSQL did not become ready for $customerCount customers."
        }
        Wait-Tcp -HostName 'localhost' -Port $postgresHostPort -TimeoutSeconds 60

        & dotnet run --project $DbProject --configuration Release
        if ($LASTEXITCODE -ne 0) {
            throw "Fixture seeding failed for $customerCount customers."
        }

        Compose @('up','-d','mcp-efcore','mcp-foundgine')
        if ($LASTEXITCODE -ne 0) {
            Compose @('ps')
            Compose @('logs','mcp-efcore')
            Compose @('logs','mcp-foundgine')
            throw "Run 5 API services failed to start for $customerCount customers."
        }

        Wait-Http 'http://localhost:4411/health/ready'
        Wait-Http 'http://localhost:4412/health/ready'

        foreach ($c in $concurrencyArray) {
            $runDir = Join-Path $tierRoot ('concurrency-{0:D3}' -f $c)
            New-Item -ItemType Directory -Force -Path $runDir | Out-Null

            $env:RUN5_CUSTOMER_COUNT = $customerCount.ToString()
            $env:RUN5_CONCURRENCY = $c.ToString()
            $env:RUN5_RUNS = $runsForTier.ToString()
            $env:RUN5_WARMUPS = $Warmups.ToString()
            $env:RUN5_REPORT_DIRECTORY = $runDir
            $env:RUN5_BATCH_SIZE = '8'
            $env:RUN5_EFCORE_MCP_URL = 'http://localhost:4411/mcp'
            $env:RUN5_MCP_URL = 'http://localhost:4412/mcp'

            Write-Host "Run 5: customers=$customerCount concurrency=$c runs=$runsForTier warmups=$Warmups"
            & dotnet run --project (Join-Path $PSScriptRoot 'Runner/Runner.csproj') -c Release
            if ($LASTEXITCODE -ne 0) {
                throw "Run 5 failed for $customerCount customers at concurrency $c (exit code $LASTEXITCODE)"
            }
        }
    }

    if ($Publish) {
        $publishScript = Join-Path $PSScriptRoot 'publish-report.ps1'
        & $publishScript -ReportRoot $Report
        if (-not $?) {
            throw 'Benchmark report publication failed.'
        }
    }
}
finally {
    $previousEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $cleanup = @(& docker compose -p $Project -f $ComposeFile down -v --remove-orphans --timeout 30 2>&1)
        $cleanupExit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousEap
    }
    if ($cleanupExit -ne 0) {
        Write-Warning "Benchmark cleanup returned exit code $cleanupExit."
        $cleanup | ForEach-Object { Write-Host $_ }
    }
}
