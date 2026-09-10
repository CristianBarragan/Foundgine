$ErrorActionPreference = "Stop"

$PipelineRoot = $PSScriptRoot

function Stop-AllBenchmarkEnvironments {
    foreach ($entry in @(
        @{ Project = "coffeebeanery-query";    Compose = (Join-Path $PipelineRoot "compose\postgres.yml") },
        @{ Project = "coffeebeanery-mutation"; Compose = (Join-Path $PipelineRoot "compose\mutation.yml") },
        @{ Project = "coffeebeanery-update";   Compose = (Join-Path $PipelineRoot "compose\update.yml") }
    )) {
        # Docker may emit harmless removal messages on stderr. Cleanup must be
        # best-effort and must not mask the actual benchmark result.
        & cmd.exe /d /c "docker compose -p $entry.Project -f `"$entry.Compose`" down -v --remove-orphans >nul 2>&1" | Out-Null
    }
}

try {
    Write-Host ""
    Write-Host "============================================"
    Write-Host " CoffeeBeanery Performance Benchmarks"
    Write-Host "============================================"
    Write-Host ""

    $Suites = @(
        "query",
        "mutation",
        "update"
    )

    foreach ($suite in $Suites) {
        $script = Join-Path $PipelineRoot "pipelines\$suite.ps1"

        Write-Host ""
        Write-Host ">>> STARTING $suite"
        Write-Host ""

        & $script

        if ($LASTEXITCODE -ne 0) {
            throw "$suite benchmark failed with exit code $LASTEXITCODE"
        }

        Write-Host ""
        Write-Host ">>> $suite completed"
    }

    Write-Host ""
    Write-Host "============================================"
    Write-Host " All benchmark suites completed"
    Write-Host "============================================"
}
finally {
    Write-Host ""
    Write-Host "Cleaning benchmark containers and PostgreSQL volumes..."
    Stop-AllBenchmarkEnvironments
    Write-Host "Benchmark environment cleanup completed."
}
