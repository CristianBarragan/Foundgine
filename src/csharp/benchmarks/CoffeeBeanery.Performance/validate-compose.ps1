$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path

Write-Host "Repository root: $repoRoot"

foreach ($suite in @("query", "mutation", "update")) {
    $compose = Join-Path $PSScriptRoot "compose\$suite.yml"

    if (-not (Test-Path $compose)) {
        throw "Missing compose file: $compose"
    }

    Write-Host "Validating $suite..."
    docker compose -f $compose config | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "Compose validation failed for $suite"
    }
}

Write-Host "All benchmark Compose files are valid."
