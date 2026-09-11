[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [int]$Rounds = 24,
    [int]$Customers = 5,
    [switch]$KeepAlive,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$securityRoot = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $securityRoot "../..")).Path
$sampleRoot = Join-Path $repoRoot "src/csharp/samples/Foundgine.SupplyChain.Advanced"
$compose = Join-Path $sampleRoot "docker-compose.yml"
$securityProject = Join-Path $securityRoot "Foundgine.RedTeam.Security.csproj"
$databaseProject = Join-Path $sampleRoot "Database/Database.csproj"
$reports = Join-Path $securityRoot "artifacts"
$semanticProject = Join-Path $sampleRoot "Semantic/Api/Mcp/Foundgine.SupplyChain.Advanced.Mcp.Api.csproj"
$semanticUrl = "http://127.0.0.1:4432"
$executionUrl = "http://127.0.0.1:4422"

$env:SUPPLY_CHAIN_CUSTOMERS = "$Customers"
$env:SupplyChainConnectionString = "Host=localhost;Port=4429;Database=foundgine_supply_chain;Username=benchmark;Password=benchmark"

New-Item -ItemType Directory -Force -Path $reports | Out-Null

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host ""; Write-Host "[$Name]" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "Step '$Name' failed with exit code $LASTEXITCODE." }
}

function Wait-Tcp([string]$HostName, [int]$Port, [int]$TimeoutSeconds = 90) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $client = [System.Net.Sockets.TcpClient]::new()
            try {
                $task = $client.ConnectAsync($HostName, $Port)
                if ($task.Wait(2000) -and $client.Connected) { return }
            } finally { $client.Dispose() }
        } catch {}
        Start-Sleep -Milliseconds 500
    }
    throw "TCP endpoint did not become ready: ${HostName}:${Port}"
}

function Wait-Http([string]$Url, [int]$TimeoutSeconds = 120) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500) { return }
        } catch {}
        Start-Sleep -Seconds 2
    }
    throw "HTTP endpoint did not become ready: $Url"
}

$semanticProcess = $null
try {
    Push-Location $repoRoot

    Write-Host "===============================================" -ForegroundColor Green
    Write-Host " Foundgine Supply Chain AI Security / Pentest " -ForegroundColor Green
    Write-Host "===============================================" -ForegroundColor Green
    Write-Host "Repo:       $repoRoot"
    Write-Host "Sample:     $sampleRoot"
    Write-Host "Rounds:     $Rounds"
    Write-Host "Customers:  $Customers"

    Invoke-Step "1/7 Verify toolchain" {
        dotnet --version
        docker --version
        docker compose version
    }

    Invoke-Step "2/7 Stop previous sample" {
        docker compose -f $compose down -v --remove-orphans
    }

    Invoke-Step "3/7 Build and start Advanced Supply Chain" {
        docker compose -f $compose up -d --build postgres mcp-foundgine
    }

    Wait-Tcp "127.0.0.1" 4429
    Wait-Http "$executionUrl/health/ready"

    Invoke-Step "4/7 Seed Advanced Supply Chain database" {
        if (-not $SkipBuild) { dotnet restore $databaseProject }
        dotnet run --project $databaseProject -c $Configuration --no-restore
    }

    # The semantic authorization lab is intentionally started separately from
    # the execution container. It shares the repo/sample code and lets the AI
    # attack the high-assurance semantic authorization boundary directly.
    Write-Host ""; Write-Host "[5/7] Start semantic authorization lab" -ForegroundColor Cyan
    $semanticLog = Join-Path $reports "semantic-api.log"
    $semanticErr = Join-Path $reports "semantic-api.err.log"
    $env:ASPNETCORE_URLS = $semanticUrl
    $semanticProcess = Start-Process dotnet `
        -ArgumentList @("run", "--project", $semanticProject, "-c", $Configuration, "--no-restore") `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $semanticLog `
        -RedirectStandardError $semanticErr `
        -PassThru

    Wait-Http "$semanticUrl/health"

    Invoke-Step "6/7 Run AI red-team agent against semantic API" {
        if (-not $SkipBuild) { dotnet restore $securityProject }
        dotnet run --project $securityProject -c $Configuration --no-restore -- `
            --base-url "$semanticUrl/" `
            --surface mcp `
            --profile semantic `
            --rounds $Rounds `
            --output (Join-Path $reports "semantic-redteam.json")
    }

    Invoke-Step "7/7 Run AI red-team agent against execution MCP" {
        dotnet run --project $securityProject -c $Configuration --no-restore -- `
            --base-url "$executionUrl/" `
            --surface mcp `
            --profile execution `
            --rounds ([Math]::Min($Rounds, 12)) `
            --output (Join-Path $reports "execution-redteam.json")
    }

    Write-Host ""; Write-Host "===============================================" -ForegroundColor Green
    Write-Host " Security run complete " -ForegroundColor Green
    Write-Host "===============================================" -ForegroundColor Green
    Write-Host "Reports: $reports"
    Write-Host "Semantic:  $(Join-Path $reports 'semantic-redteam.json')"
    Write-Host "Execution: $(Join-Path $reports 'execution-redteam.json')"

    if ($KeepAlive) {
        Write-Host ""; Write-Host "Keeping Docker + semantic API alive. Press Ctrl+C to stop." -ForegroundColor Yellow
        while ($true) { Start-Sleep -Seconds 5 }
    }
}
finally {
    Pop-Location -ErrorAction SilentlyContinue

    if ($semanticProcess -and -not $KeepAlive) {
        try { Stop-Process -Id $semanticProcess.Id -Force -ErrorAction SilentlyContinue } catch {}
    }

    if (-not $KeepAlive) {
        Write-Host ""; Write-Host "Cleaning up Supply Chain containers..." -ForegroundColor Yellow
        docker compose -f $compose down -v --remove-orphans
    }
}
