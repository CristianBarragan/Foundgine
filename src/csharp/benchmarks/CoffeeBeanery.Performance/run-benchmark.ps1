$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

Write-Host '== Clean benchmark environment ==' -ForegroundColor Cyan
cmd.exe /d /c "docker compose -f .\docker-compose.benchmark.yml down -v --remove-orphans >nul 2>&1" | Out-Null

Write-Host '== Build benchmark images from a clean source tree ==' -ForegroundColor Cyan
docker compose -f .\docker-compose.benchmark.yml build --no-cache

Write-Host '== Run correctness preflight + benchmark ==' -ForegroundColor Cyan
docker compose -f .\docker-compose.benchmark.yml up

$exitCode = $LASTEXITCODE

Write-Host '== Cleanup ==' -ForegroundColor Cyan
cmd.exe /d /c "docker compose -f .\docker-compose.benchmark.yml down -v --remove-orphans >nul 2>&1" | Out-Null

exit $exitCode
