$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$compose = Join-Path $root "src/csharp/samples/Foundgine.SupplyChain.Advanced/docker-compose.yml"
Get-Process dotnet -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "*dotnet*" } |
    Out-Null

docker compose -f $compose down -v --remove-orphans
Write-Host "Foundgine Supply Chain security environment stopped." -ForegroundColor Green
