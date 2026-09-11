$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$compose = Join-Path $root "docker-compose.yml"

$env:SUPPLY_CHAIN_CUSTOMERS = if ($env:SUPPLY_CHAIN_CUSTOMERS) { $env:SUPPLY_CHAIN_CUSTOMERS } else { "5" }
$env:SUPPLY_CHAIN_STEPS = if ($env:SUPPLY_CHAIN_STEPS) { $env:SUPPLY_CHAIN_STEPS } else { "25" }
$env:SUPPLY_CHAIN_SEED = if ($env:SUPPLY_CHAIN_SEED) { $env:SUPPLY_CHAIN_SEED } else { "20260823" }
$env:SUPPLY_CHAIN_MCP_URL = if ($env:SUPPLY_CHAIN_MCP_URL) { $env:SUPPLY_CHAIN_MCP_URL } else { "http://localhost:4422/mcp" }
$env:SUPPLY_CHAIN_REPORT_DIRECTORY = Join-Path $root "reports"

Push-Location $root

try {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Foundgine Supply Chain E2E" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Root:        $root"
    Write-Host "Customers:   $env:SUPPLY_CHAIN_CUSTOMERS"
    Write-Host "Steps:       $env:SUPPLY_CHAIN_STEPS"
    Write-Host "Seed:        $env:SUPPLY_CHAIN_SEED"
    Write-Host "MCP URL:     $env:SUPPLY_CHAIN_MCP_URL"
    Write-Host ""

    Write-Host "[1/8] Checking Docker..." -ForegroundColor Yellow
    docker --version
    docker compose version

    function Wait-Tcp([string]$HostName, [int]$Port, [int]$TimeoutSeconds = 60) {
        # docker compose's depends_on: condition: service_healthy only proves
        # Postgres is healthy *inside* the container. It doesn't prove the
        # host-published port ($HostName:$Port, the exact endpoint the .NET
        # seeder's Npgsql connection uses below) is accepting external
        # connections yet - see the identical fix in Run4/run-run4.ps1 and
        # Run5/run-run5.ps1, which hit this as an Npgsql connect timeout.
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
        throw "Host port did not accept connections: ${HostName}:${Port}"
    }

    function Wait-Http([string]$Url, [int]$TimeoutSeconds = 120) {
        # mcp-foundgine has no healthcheck in docker-compose.yml, so
        # `docker compose up` returns once its container process has
        # *started* - not once the ASP.NET app inside is actually accepting
        # HTTP requests. Without this, the AI agent step below can hit the
        # MCP endpoint before it's listening.
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            try {
                $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
                if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500) { return }
            } catch {}
            Start-Sleep -Seconds 2
        }
        docker compose -f $compose ps
        throw "Endpoint did not become ready: $Url"
    }

    Write-Host ""
    Write-Host "[2/8] Stopping previous Supply Chain containers..." -ForegroundColor Yellow
    docker compose -f $compose down -v --remove-orphans
    if ($LASTEXITCODE -ne 0) { throw "Failed to stop previous Supply Chain containers (exit code $LASTEXITCODE)." }

    Write-Host ""
    Write-Host "[3/8] Building and starting PostgreSQL + Foundgine MCP..." -ForegroundColor Yellow
    docker compose -f $compose up -d --build postgres mcp-foundgine
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed to build/start Supply Chain services (exit code $LASTEXITCODE)." }

    Write-Host ""
    Write-Host "[4/8] Current container state..." -ForegroundColor Yellow
    docker compose -f $compose ps

    Write-Host ""
    Write-Host "[5/8] Seeding PostgreSQL..." -ForegroundColor Yellow

    $env:SupplyChainConnectionString = "Host=localhost;Port=4429;Database=foundgine_supply_chain;Username=benchmark;Password=benchmark"

    Wait-Tcp -HostName 'localhost' -Port 4429 -TimeoutSeconds 60

    dotnet restore Database/Database.csproj
    if ($LASTEXITCODE -ne 0) { throw "Database restore failed with exit code $LASTEXITCODE." }

    dotnet run `
        --project Database/Database.csproj `
        -c Release `
        --no-restore

    if ($LASTEXITCODE -ne 0) {
        throw "Database seeding failed with exit code $LASTEXITCODE."
    }

    Write-Host ""
    Write-Host "[6/8] Starting AI agent..." -ForegroundColor Yellow

    Wait-Http -Url 'http://localhost:4422/health/ready' -TimeoutSeconds 120

    dotnet restore Agent/Agent.csproj
    if ($LASTEXITCODE -ne 0) { throw "Agent restore failed with exit code $LASTEXITCODE." }

    dotnet run `
        --project Agent/Agent.csproj `
        -c Release `
        --no-restore

    if ($LASTEXITCODE -ne 0) {
        throw "Agent failed with exit code $LASTEXITCODE."
    }

    Write-Host ""
    Write-Host "[7/8] Running Supply Chain PenTest regression cases against the benchmark PostgreSQL..." -ForegroundColor Yellow
    $pentestProject = Join-Path $root "../../benchmarks/AgentEndToEnd/Fixtures/SupplyChain.PenTest/Tests/Foundgine.SupplyChain.PenTest.Tests.csproj"
    $pentestResults = Join-Path $env:SUPPLY_CHAIN_REPORT_DIRECTORY "supply-chain-pentest.trx"
    $env:FOUNDGINE_SUPPLYCHAIN_PENTEST = "1"

    dotnet restore $pentestProject
    if ($LASTEXITCODE -ne 0) { throw "Supply Chain PenTest restore failed with exit code $LASTEXITCODE." }

    $pentestTimer = [System.Diagnostics.Stopwatch]::StartNew()
    dotnet test $pentestProject `
        -c Release `
        --no-restore `
        --filter "FullyQualifiedName~GraphPenetrationTests|FullyQualifiedName~McpPenetrationTests" `
        --results-directory $env:SUPPLY_CHAIN_REPORT_DIRECTORY `
        --logger "trx;LogFileName=supply-chain-pentest.trx"
    $pentestExitCode = $LASTEXITCODE
    $pentestTimer.Stop()

    # Always merge the measured TRX results into the already-generated E2E report
    # before surfacing a test failure. This leaves one report containing both the
    # stochastic agent workload and the deterministic security regression cases.
    & (Join-Path $root "merge-supply-chain-pentest-report.ps1") `
        -ReportPath (Join-Path $env:SUPPLY_CHAIN_REPORT_DIRECTORY "supply-chain-report.json") `
        -PentestResultsPath $pentestResults `
        -SuiteDurationMs ([Math]::Round($pentestTimer.Elapsed.TotalMilliseconds, 1))

    if ($pentestExitCode -ne 0) {
        throw "Supply Chain PenTest failed with exit code $pentestExitCode."
    }

    Write-Host ""
    Write-Host "[8/8] Supply Chain E2E + PenTest measurement complete." -ForegroundColor Green
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " Supply Chain E2E PASSED" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host " Supply Chain E2E FAILED" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host $_ -ForegroundColor Red

    Write-Host ""
    Write-Host "Container state:" -ForegroundColor Yellow
    docker compose -f $compose ps -a

    Write-Host ""
    Write-Host "Recent logs:" -ForegroundColor Yellow
    docker compose -f $compose logs --tail 100

    exit 1
}
finally {
    Pop-Location
}