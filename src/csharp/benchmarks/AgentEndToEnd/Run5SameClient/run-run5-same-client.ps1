[CmdletBinding()]
param(
    [int[]]$CustomerCounts = @(10,100,1000,10000),
    [int[]]$Concurrency = @(8,16,32,64),
    [Alias("Runs")] [int[]]$RunsPerTier = @(30,30,30,30),
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
$Project = 'foundgine-run5-same-client'
$DbProject = Join-Path $PSScriptRoot 'Database/Database.csproj'
$Report = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $Report | Out-Null

$CustomerCounts = @($CustomerCounts)
$RunsPerTier = @($RunsPerTier)
$Concurrency = @($Concurrency)

if ($CustomerCounts.Count -ne $RunsPerTier.Count) {
    throw 'CustomerCounts and RunsPerTier must have the same number of entries.'
}
if ($CustomerCounts.Count -lt 1) {
    throw 'At least one customer tier is required.'
}
$invalidConcurrency = @($Concurrency | Where-Object { $_ -lt 1 })
if ($invalidConcurrency.Count -gt 0) {
    throw 'Concurrency values must be positive.'
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

try {
    Compose @('down','-v','--remove-orphans')
    Compose @('build','mcp-efcore','mcp-foundgine')

    for ($index = 0; $index -lt $CustomerCounts.Count; $index++) {
        $customerCount = $CustomerCounts[$index]
        $runsForTier = $RunsPerTier[$index]
        $tier = '{0:D5}-customers' -f $customerCount
        $tierRoot = Join-Path $Report $tier
        New-Item -ItemType Directory -Force -Path $tierRoot | Out-Null

        Write-Host ''
        Write-Host '============================================'
        Write-Host " Run 5 Same Client fixture tier: $customerCount customers"
        Write-Host '============================================'

        # Fresh PostgreSQL database for every customer tier.
        Compose @('down','-v','--remove-orphans','--timeout','30')

        $postgresHostPort = Get-FreeTcpPort
        $env:POSTGRES_HOST_PORT = $postgresHostPort.ToString()
        $connectionString = "Host=localhost;Port=$postgresHostPort;Database=foundgine_benchmark;Username=benchmark;Password=benchmark"
        $env:BankingConnectionString = $connectionString
        $env:TRANSFER_FUNDS_CUSTOMERS = $customerCount.ToString()

        Write-Host "Run 5 Same Client PostgreSQL host port: $postgresHostPort"
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

        & dotnet run --project $DbProject --configuration Release
        if ($LASTEXITCODE -ne 0) {
            throw "Fixture seeding failed for $customerCount customers."
        }

        Compose @('up','-d','mcp-efcore','mcp-foundgine')
        if ($LASTEXITCODE -ne 0) {
            Compose @('ps')
            Compose @('logs','mcp-efcore')
            Compose @('logs','mcp-foundgine')
            throw "Run 5 Same Client API services failed to start for $customerCount customers."
        }

        Wait-Http 'http://localhost:4411/health/ready'
        Wait-Http 'http://localhost:4412/health/ready'

        foreach ($c in $Concurrency) {
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

            Write-Host "Run 5 Same Client: customers=$customerCount concurrency=$c runs=$runsForTier warmups=$Warmups"
            & dotnet run --project (Join-Path $PSScriptRoot 'Runner/Runner.csproj') -c Release
            if ($LASTEXITCODE -ne 0) {
                throw "Run 5 Same Client failed for $customerCount customers at concurrency $c (exit code $LASTEXITCODE)"
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
