[CmdletBinding()]
param(
    [ValidateSet('replay','live')]
    [string]$Mode = 'replay',
    [int]$Warmups = 1,
    [int]$Runs = 3,
    [switch]$NoInfrastructure,
    [switch]$KeepInfrastructure,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$BenchmarkRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $BenchmarkRoot '..\..')).Path
$ComposeFile = Join-Path $RepoRoot 'benchmarks\CoffeeBeanery.Performance\docker-compose.benchmark.yml'
$ProjectFile = Join-Path $BenchmarkRoot 'Foundgine.AgentEndToEnd.Benchmark.csproj'
$PublishScript = Join-Path $BenchmarkRoot 'publish-report.ps1'
$ReportDirectory = Join-Path $BenchmarkRoot 'artifacts\agent-benchmark'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found on PATH. Install the .NET 9 SDK and reopen PowerShell.'
}

if (-not $NoInfrastructure) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'docker was not found on PATH. Install/start Docker Desktop or use -NoInfrastructure with an existing PostgreSQL/Foundgine environment.'
    }

    Write-Host ''
    Write-Host 'Validating benchmark Docker Compose configuration...' -ForegroundColor Cyan
    & docker compose -f $ComposeFile config --quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Invalid Docker Compose configuration: $ComposeFile"
    }

    Write-Host 'Starting CoffeeBeanery PostgreSQL + Foundgine warm API...' -ForegroundColor Cyan
    & docker compose -f $ComposeFile up -d --build --force-recreate postgres database foundgine-warm
    if ($LASTEXITCODE -ne 0) { throw 'Failed to start the benchmark infrastructure.' }

    Write-Host 'Waiting for Foundgine warm API...' -ForegroundColor DarkGray
    $healthUrl = 'http://localhost:4302/health'
    $deadline = (Get-Date).AddMinutes(3)
    do {
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                Write-Host 'Foundgine warm API is ready.' -ForegroundColor Green
                break
            }
        } catch {
            Start-Sleep -Seconds 2
        }
    } while ((Get-Date) -lt $deadline)

    if ((Get-Date) -ge $deadline) {
        Write-Host 'Foundgine warm API did not become ready. Recent service logs:' -ForegroundColor Yellow
        & docker compose -f $ComposeFile logs --tail 80 foundgine-warm
        throw 'Benchmark infrastructure did not become ready within 3 minutes.'
    }
}

$env:AGENT_BENCHMARK_MODE = $Mode
$env:AGENT_BENCHMARK_WARMUPS = [string]$Warmups
$env:AGENT_BENCHMARK_RUNS = [string]$Runs
$env:AGENT_BENCHMARK_REPORT_DIRECTORY = $ReportDirectory

if (-not $env:BankingConnectionString) {
    $env:BankingConnectionString = 'Host=localhost;Port=55432;Database=foundgine_benchmark;Username=benchmark;Password=benchmark'
}

if (-not $env:FOUNDGINE_GRAPHQL_URL) {
    $env:FOUNDGINE_GRAPHQL_URL = 'http://localhost:4302/graphql/warm'
}

if ($Mode -eq 'live') {
    foreach ($name in @('AGENT_MODEL_ENDPOINT','AGENT_MODEL')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { continue }
        throw "Live mode requires `$env:$name. Example: `$env:$name = '...'"
    }
}

Write-Host ''
Write-Host '===============================================' -ForegroundColor Cyan
Write-Host ' Foundgine Agent End-to-End Benchmark' -ForegroundColor Cyan
Write-Host '===============================================' -ForegroundColor Cyan
Write-Host "Mode:       $Mode"
Write-Host "Warmups:    $Warmups"
Write-Host "Measured:   $Runs"
Write-Host "Postgres:   $env:BankingConnectionString"
Write-Host "Foundgine:  $env:FOUNDGINE_GRAPHQL_URL"
Write-Host "Report:     $ReportDirectory"
Write-Host ''

Push-Location $RepoRoot
try {
    & dotnet run --project $ProjectFile --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw "Agent benchmark failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

if ($Publish) {
    & $PublishScript -Report (Join-Path $ReportDirectory 'agent-benchmark.json')
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark report publication failed.' }
}

if (-not $KeepInfrastructure -and -not $NoInfrastructure) {
    Write-Host ''
    Write-Host 'Stopping benchmark infrastructure...' -ForegroundColor DarkGray
    & docker compose -f $ComposeFile down
}

Write-Host ''
Write-Host 'Benchmark complete.' -ForegroundColor Green
Write-Host "JSON:     $(Join-Path $ReportDirectory 'agent-benchmark.json')"
Write-Host "Markdown: $(Join-Path $ReportDirectory 'agent-benchmark.md')"
