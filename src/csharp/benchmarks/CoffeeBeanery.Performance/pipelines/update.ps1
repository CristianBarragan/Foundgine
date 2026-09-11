$ErrorActionPreference = "Stop"
$ComposeFile = Join-Path $PSScriptRoot "..\compose\update.yml"
$RunId = "{0}-{1}" -f $PID, ([Guid]::NewGuid().ToString("N").Substring(0, 8))
$ProjectName = "coffeebeanery-update-$RunId"

Write-Host ""
Write-Host "============================================"
Write-Host " CoffeeBeanery Update benchmark"
Write-Host "============================================"
Write-Host ""

try {
    docker compose -p $ProjectName -f $ComposeFile up -d --build
    if ($LASTEXITCODE -ne 0) { throw "Failed to start update benchmark environment." }

    docker compose -p $ProjectName -f $ComposeFile wait loader
    $code = docker inspect -f '{{.State.ExitCode}}' "${ProjectName}-loader-1"
    if (-not $code) { $code = 1 }
    exit ([int]$code)
}
finally {
    Write-Host ""
    Write-Host "Stopping update benchmark environment..."
    cmd.exe /d /c "docker compose -p $ProjectName -f `"$ComposeFile`" down -v --remove-orphans >nul 2>&1" | Out-Null
}
