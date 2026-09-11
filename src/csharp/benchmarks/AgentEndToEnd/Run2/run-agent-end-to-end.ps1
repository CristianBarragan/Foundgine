[CmdletBinding()]
param(
    [object]$Concurrency = @(8,16,32,64),
    [ValidateSet('replay','live')]
    [string]$Mode = 'replay',

    [int[]]$CustomerCounts = @(10, 100, 1000, 10000),

    [int[]]$RunsPerTier = @(10, 10, 10, 10),

    [int]$Warmups = 2,

    [int]$RelationshipsPerCustomer = 4,

    [int]$ContractsPerRelationship = 3,

    [int]$TransactionsPerContract = 4,

    [string]$ReportRoot = (Join-Path $PSScriptRoot 'artifacts'),

    [string]$ConnectionString = '',

    [string]$FoundgineUrl = 'http://localhost:4302/graphql/warm',

    [string]$FoundgineReadyUrl = 'http://localhost:4302/health/ready',

    [switch]$KeepDatabase,

    [switch]$SkipDockerBuild,

    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
# PowerShell 7.3+ promotes anything a native command (docker, docker compose,
# etc.) writes to stderr into an ErrorRecord and, with $ErrorActionPreference
# = 'Stop', that becomes a terminating error - even for routine lifecycle
# output like "Container ... Stopping" that docker compose intentionally
# writes to stderr. Redirecting with 2>&1 does NOT prevent this by itself;
# it just merges the stream, the promotion still happens. Turn the
# promotion off so only actual non-zero exit codes (checked explicitly
# below via $LASTEXITCODE) are treated as failures.
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest

$PowerShellHost = $null
$PostgresHostPort = $null
$PwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue
if ($null -ne $PwshCommand) {
    $PowerShellHost = $PwshCommand.Source
}
if ([string]::IsNullOrWhiteSpace($PowerShellHost)) {
    $WindowsPowerShellCommand = Get-Command powershell -ErrorAction SilentlyContinue
    if ($null -ne $WindowsPowerShellCommand) {
        $PowerShellHost = $WindowsPowerShellCommand.Source
    }
}
if ([string]::IsNullOrWhiteSpace($PowerShellHost)) {
    throw 'Neither pwsh nor Windows PowerShell was found. Install PowerShell 7 or ensure powershell.exe is on PATH.'
}

# Normalize concurrency defensively. PowerShell can pass an explicitly
# constructed array as a single object in some invocation forms; accepting
# object here avoids parameter-binding failure before the script can validate it.
$Concurrency = @($Concurrency) |
    ForEach-Object {
        if ($_ -is [System.Array]) {
            $_ | ForEach-Object { [int]$_ }
        }
        else {
            [int]$_
        }
    }

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '../../../../..')
$ComposeFile = Join-Path $PSScriptRoot 'docker-compose.yml'
$DatabaseProject = Join-Path $RepoRoot 'src/csharp/benchmarks/CoffeeBeanery.Performance/CoffeeBeanery.Database/CoffeeBeanery.Database.csproj'
$BenchmarkProject = Join-Path $RepoRoot 'src/csharp/benchmarks/AgentEndToEnd/Run2/Foundgine.AgentEndToEnd.csproj'
$MetricsScript = Join-Path $RepoRoot 'src/csharp/benchmarks/AgentEndToEnd/scripts/docker-metrics.ps1'
$MetricsSummaryScript = Join-Path $RepoRoot 'src/csharp/benchmarks/AgentEndToEnd/scripts/summarize-docker-metrics.ps1'
$ComposeProjectName = 'foundgine-agent-e2e'

function Restore-BenchmarkProjects {
    Write-Host 'Restoring benchmark projects...'

    & dotnet restore $DatabaseProject --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet restore failed for CoffeeBeanery.Database."
    }

    & dotnet restore $BenchmarkProject --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet restore failed for Foundgine.AgentEndToEnd."
    }
}

if ($KeepDatabase -and $CustomerCounts.Count -ne 1) {
    throw '-KeepDatabase can only be used with a single customer tier. Each tier must normally start from a fresh PostgreSQL volume.'
}

if ($CustomerCounts.Count -ne $RunsPerTier.Count) {
    throw "CustomerCounts and RunsPerTier must contain the same number of values. Received $($CustomerCounts.Count) and $($RunsPerTier.Count)."
}

foreach ($count in $CustomerCounts) {
    if ($count -lt 1) { throw "CustomerCounts must contain only positive values." }
}

foreach ($runs in $RunsPerTier) {
    if ($runs -lt 1) { throw "RunsPerTier must contain only positive values." }
}

foreach ($c in $Concurrency) {
    if ($c -lt 1) { throw "Concurrency must contain only positive values." }
}

New-Item -ItemType Directory -Force -Path $ReportRoot | Out-Null

function Invoke-Compose {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & docker compose -p $ComposeProjectName -f $ComposeFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose failed with exit code $LASTEXITCODE. Arguments: $($Arguments -join ' ')"
    }
}

function Wait-Postgres {
    Write-Host 'Waiting for PostgreSQL...'
    for ($i = 1; $i -le 90; $i++) {
        & docker compose -p $ComposeProjectName -f $ComposeFile exec -T postgres pg_isready -U benchmark -d foundgine_benchmark 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            return
        }
        Start-Sleep -Seconds 1
    }
    Invoke-Compose -Arguments @('ps')
    Invoke-Compose -Arguments @('logs', 'postgres')
    throw 'PostgreSQL did not become ready within 90 seconds.'
}

function Wait-Http {
    param(
        [Parameter(Mandatory)][string]$Url,
        [int]$TimeoutSeconds = 180
    )

    Write-Host "Waiting for $Url ..."
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return
            }
        }
        catch {
            # Service is still starting.
        }
        Start-Sleep -Seconds 2
    }

    Invoke-Compose -Arguments @('ps')
    Invoke-Compose -Arguments @('logs', 'foundgine-warm')
    throw "Foundgine warm endpoint did not become reachable: $Url"
}

function Stop-Stack {
    # $ErrorActionPreference = 'Stop' at script scope means any text docker
    # compose writes to stderr - including routine lifecycle messages like
    # "Container ... Stopping" - gets wrapped into a NativeCommandError and
    # terminates the script, even though the command itself succeeds (exit
    # code 0). Merging streams with 2>&1 does NOT prevent this by itself.
    # Temporarily relax to 'Continue' around the native call so only the
    # explicit $LASTEXITCODE check below decides success/failure, then
    # restore the caller's preference immediately after.
    $previousEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($KeepDatabase) {
            Write-Host 'Stopping application containers but preserving PostgreSQL volume...'
            $output = @(& docker compose -p $ComposeProjectName -f $ComposeFile stop foundgine-warm 2>&1)
            $exitCode = $LASTEXITCODE
            if ($exitCode -ne 0) {
                Write-Warning "Docker compose stop returned exit code $exitCode."
                $output | ForEach-Object { Write-Host $_ }
            }
            return
        }

        Write-Host 'Removing benchmark containers and PostgreSQL volume...'
        $output = @(& docker compose -p $ComposeProjectName -f $ComposeFile down -v --remove-orphans 2>&1)
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            Write-Warning "Docker compose cleanup returned exit code $exitCode."
            $output | ForEach-Object { Write-Host $_ }
        }
    }
    finally {
        $ErrorActionPreference = $previousEap
    }
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try { $listener.Start(); return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}

function Start-Postgres {
    $script:PostgresHostPort = Get-FreeTcpPort
    $env:POSTGRES_HOST_PORT = $script:PostgresHostPort.ToString()
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        $script:ConnectionString = "Host=localhost;Port=$($script:PostgresHostPort);Database=foundgine_benchmark;Username=benchmark;Password=benchmark"
    } else {
        $script:ConnectionString = $ConnectionString -replace 'Port=\d+', "Port=$($script:PostgresHostPort)"
    }
    Write-Host "Starting PostgreSQL 17 benchmark container on host port $($script:PostgresHostPort)..."
    Invoke-Compose -Arguments @('up', '-d', 'postgres')
    Wait-Postgres
}

function Seed-Fixture {
    param([Parameter(Mandatory)][int]$CustomerCount)

    Write-Host "Seeding fixture: $CustomerCount customers ..."

    $env:BankingConnectionString = $script:ConnectionString
    $env:COFFEEBEANERY_CONNECTION = $script:ConnectionString
    $env:COFFEEBEANERY_CUSTOMERS = $CustomerCount.ToString()
    $env:COFFEEBEANERY_RELATIONSHIPS_PER_CUSTOMER = $RelationshipsPerCustomer.ToString()
    $env:COFFEEBEANERY_CONTRACTS_PER_RELATIONSHIP = $ContractsPerRelationship.ToString()
    $env:COFFEEBEANERY_TRANSACTIONS_PER_CONTRACT = $TransactionsPerContract.ToString()

    & dotnet run --project $DatabaseProject --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture seeding failed for $CustomerCount customers."
    }
}

function Start-Foundgine {
    # Dynamically allocate the host port, same as Start-Postgres above.
    # This container was previously fixed to host port 4302, which meant
    # back-to-back runs (this loop tears the stack down and restarts it once
    # per customer tier, and the orchestrator chains Run1 -> Run2 -> Run3
    # with no gap) could race Docker's release of a just-stopped container's
    # port binding, failing with a "port already in use"-style startup
    # error - intermittently, and worse the tighter the gap between teardown
    # and the next startup.
    $script:FoundgineHostPort = Get-FreeTcpPort
    $env:FOUNDGINE_HOST_PORT = $script:FoundgineHostPort.ToString()
    $script:FoundgineUrl = "http://localhost:$($script:FoundgineHostPort)/graphql/warm"
    $script:FoundgineReadyUrl = "http://localhost:$($script:FoundgineHostPort)/health/ready"

    if (-not $SkipDockerBuild) {
        Write-Host "Building and starting Foundgine warm benchmark API on host port $($script:FoundgineHostPort)..."
        Invoke-Compose -Arguments @('build', 'foundgine-warm')
    }
    else {
        Write-Host "Starting Foundgine warm benchmark API on host port $($script:FoundgineHostPort) without rebuilding..."
    }

    Invoke-Compose -Arguments @('up', '-d', '--no-deps', 'foundgine-warm')
    Wait-Http -Url $script:FoundgineReadyUrl
}

function Run-AgentBenchmark {
    param(
        [Parameter(Mandatory)][int]$CustomerCount,
        [Parameter(Mandatory)][int]$Runs,
        [Parameter(Mandatory)][int]$Concurrency
    )

    $tierName = '{0:D5}-customers' -f $CustomerCount
    $tierDirectory = Join-Path $ReportRoot $tierName
    $tierDirectory = Join-Path $tierDirectory ('concurrency-{0:D3}' -f $Concurrency)
    New-Item -ItemType Directory -Force -Path $tierDirectory | Out-Null
    $metricsCsv = Join-Path $tierDirectory 'docker-metrics.csv'
    $metricsJson = Join-Path $tierDirectory 'docker-metrics-summary.json'
    $metricsStop = Join-Path $tierDirectory '.docker-metrics.stop'
    Remove-Item $metricsStop -Force -ErrorAction SilentlyContinue
    $metricsProcess = Start-Process -FilePath $PowerShellHost -ArgumentList @('-NoProfile','-File',$MetricsScript,'-ComposeFile',$ComposeFile,'-ProjectName',$ComposeProjectName,'-Services','postgres,foundgine-warm','-OutputCsv',$metricsCsv,'-StopFile',$metricsStop,'-IntervalMs','1000') -PassThru

    Write-Host ''
    Write-Host '============================================'
    Write-Host " Agent benchmark: $CustomerCount customers / $Runs runs"
    Write-Host '============================================'

    $env:AGENT_BENCHMARK_MODE = $Mode
    $env:AGENT_BENCHMARK_RUNS = $Runs.ToString()
    $env:AGENT_BENCHMARK_CONCURRENCY = $Concurrency.ToString()
    $env:AGENT_BENCHMARK_CUSTOMER_COUNT = $CustomerCount.ToString()
    $env:AGENT_BENCHMARK_WARMUPS = $Warmups.ToString()
    $env:AGENT_BENCHMARK_CUSTOMER_ID = '1'
    $env:AGENT_BENCHMARK_REPORT_DIRECTORY = $tierDirectory
    $env:FOUNDGINE_GRAPHQL_URL = $FoundgineUrl
    $env:BankingConnectionString = $script:ConnectionString

    # Capture stdout/stderr without cmd.exe so this runner works on both
    # Windows and Linux-hosted GitHub runners. PowerShell's redirection keeps
    # stderr in the same log while the native process exit code remains
    # available through $LASTEXITCODE.
    $logPath = Join-Path $tierDirectory 'agent-benchmark-console.log'
    & dotnet run --project $BenchmarkProject --configuration Release --no-restore *> $logPath
    $exitCode = $LASTEXITCODE

    if (Test-Path $logPath) {
        Get-Content -Path $logPath | ForEach-Object { Write-Host $_ }
    }

    if ($exitCode -ne 0) {
        $tail = if (Test-Path $logPath) { (Get-Content -Path $logPath | Select-Object -Last 40) -join [Environment]::NewLine } else { '<no benchmark console log was produced>' }
        New-Item -ItemType File -Force -Path $metricsStop | Out-Null
        try { Wait-Process -Id $metricsProcess.Id -Timeout 10 -ErrorAction SilentlyContinue } catch {}
        if (Test-Path $metricsCsv) { & $PowerShellHost -NoProfile -File $MetricsSummaryScript -InputCsv $metricsCsv -OutputJson $metricsJson }
        throw "Agent end-to-end benchmark failed for $CustomerCount customers at concurrency $Concurrency (exit code $exitCode). Console log: $logPath`n$tail"
    }

    New-Item -ItemType File -Force -Path $metricsStop | Out-Null
    try { Wait-Process -Id $metricsProcess.Id -Timeout 10 -ErrorAction SilentlyContinue } catch {}
    if (Test-Path $metricsCsv) { & $PowerShellHost -NoProfile -File $MetricsSummaryScript -InputCsv $metricsCsv -OutputJson $metricsJson }
    Remove-Item $metricsStop -Force -ErrorAction SilentlyContinue
}

try {
    Write-Host ''
    Write-Host '============================================'
    Write-Host ' Foundgine Agent End-to-End Performance Run'
    Write-Host '============================================'
    Write-Host "Mode:                    $Mode"
    Write-Host "Customer tiers:          $($CustomerCounts -join ', ')"
    Write-Host "Concurrency:             $($Concurrency -join ", ")"
Write-Host "Runs per tier:           $($RunsPerTier -join ', ')"
    Write-Host "Warmups per flow:        $Warmups"
    Write-Host "Relationships/customer:  $RelationshipsPerCustomer"
    Write-Host "Contracts/relationship:  $ContractsPerRelationship"
    Write-Host "Transactions/contract:   $TransactionsPerContract"
    Write-Host "Reports:                 $ReportRoot"
    Write-Host ''

    & docker version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker is required for the benchmark runner.'
    }

    # The runner uses --no-restore below for deterministic execution. Restore
    # explicitly first so a clean checkout does not fail with NETSDK1004 when
    # project.assets.json is absent.
    Restore-BenchmarkProjects

    Stop-Stack

    for ($index = 0; $index -lt $CustomerCounts.Count; $index++) {
        $customerCount = $CustomerCounts[$index]
        $runs = $RunsPerTier[$index]

        # Each tier starts from a fresh PostgreSQL volume so data volume is the
        # only intentional change between tiers. This avoids benchmark pollution
        # from the preceding tier.
        Stop-Stack
        Start-Postgres
        Seed-Fixture -CustomerCount $customerCount
        Start-Foundgine
        foreach ($concurrency in $Concurrency) {
            # This scenario is a stateful query->mutate->query->mutate->query
            # sequence per customer (see STATEFUL-SCENARIO.md). Program.cs
            # will reuse customer IDs across concurrent workers rather than
            # crash when concurrency exceeds customer count, but doing so
            # lets multiple workers race to mutate/verify the SAME customer
            # row at once, which corrupts the expected-state invariants the
            # benchmark exists to check. STATEFUL-SCENARIO.md recommends at
            # least as many isolated customers as the concurrency level (64+,
            # ideally 128) - skip rather than silently produce an invalid
            # comparison.
            if ($concurrency -gt $customerCount) {
                Write-Host ''
                Write-Host ">>> Concurrency tier: $concurrency (skipped: exceeds customer count $customerCount; customer IDs would be reused across concurrent workers)"
                continue
            }

            Write-Host ''
            Write-Host ">>> Concurrency tier: $concurrency"
            Run-AgentBenchmark -CustomerCount $customerCount -Runs $runs -Concurrency $concurrency
        }
    }

    Write-Host ''
    Write-Host '============================================'
    Write-Host ' Agent end-to-end benchmark completed'
    Write-Host '============================================'
    Write-Host "Reports written to: $ReportRoot"
    if ($Publish) {
        $publishScript = Join-Path $PSScriptRoot 'publish-report.ps1'
        & $publishScript -ReportRoot $ReportRoot
        if ($LASTEXITCODE -ne 0) { throw 'Benchmark report publication failed.' }
    }
}
finally {
    if (-not $KeepDatabase) {
        try {
            Stop-Stack
        }
        catch {
            # Cleanup must never replace the actual benchmark failure.
            Write-Warning "Benchmark cleanup failed: $($_.Exception.Message)"
        }
    }
}