[CmdletBinding()]
param(
    [ValidateSet('replay','live')]
    [string]$Mode = 'replay',

    [string]$CustomerCounts = '10,100,1000,10000',

    [string]$Concurrency = '8,16,32,64',

    [int]$Warmups = 5,

    [int]$Runs = 30,

    [switch]$KeepDatabase,

    [switch]$SkipDockerBuild,

    [switch]$Publish,

    # Run1-5/5SameClient share one docker-compose-backed infra pattern and a
    # live conventional-vs-Foundgine comparison. SupplyChain E2E and the
    # SupplyChain.Semantic pipeline-weight benchmark don't fit that pattern
    # (different compose stack / no docker at all, and no conventional
    # counterpart to compare against - see each report's "efficiencyEstimate"
    # for a MODELED, not measured, reduction estimate instead). They're
    # opt-in here so a plain run-all keeps its existing behavior and runtime.
    [switch]$IncludeSupplyChain,
    [switch]$IncludeSemanticPipeline,

    # CI smoke test: exercises the whole pipeline (seed -> run -> report)
    # end to end with the smallest possible footprint. Forces a single
    # customer at concurrency 1, never publishes to docs-site/assets, and
    # deletes every artifact it produces once the run finishes - success or
    # failure - so the smoke test never leaves files behind in the repo.
    # Overrides -CustomerCounts/-Concurrency/-Warmups/-Runs/-Publish when set.
    [switch]$Smoke
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$repoRootForSemantic = (Resolve-Path (Join-Path $root '../..')).Path

if ($Smoke) {
    if ($Publish) {
        Write-Warning '-Smoke overrides -Publish: smoke runs never publish to docs-site/assets.'
    }
    $CustomerCounts = '1'
    $Concurrency = '1'
    $Warmups = 0
    $Runs = 1
    $Publish = $false
    if ($IncludeSupplyChain -or $IncludeSemanticPipeline) {
        Write-Warning '-Smoke overrides -IncludeSupplyChain/-IncludeSemanticPipeline: smoke runs stick to the Run1-5 footprint.'
    }
    $IncludeSupplyChain = $false
    $IncludeSemanticPipeline = $false
}

# Only Run1/Run2/Run3 share the replay/live "Mode" concept. Run4 has its own
# unrelated Mode (agent/protocol/both, default 'both') and Run5/Run5SameClient
# don't take a Mode at all - forwarding this orchestrator's -Mode to any of
# them fails parameter binding (ValidateSet rejection for Run4, "parameter
# cannot be found" for Run5/Run5SameClient). Build args per run instead of
# blindly splatting the same set at every script.
$runsToExecute = @('Run1', 'Run2', 'Run3', 'Run4', 'Run5', 'Run5SameClient')
$modeCapableRuns = @('Run1', 'Run2', 'Run3')

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' Foundgine Agent End-to-End Benchmark Suite' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host "Smoke:          $Smoke"
Write-Host "Mode:           $Mode (Run4/Run5/Run5SameClient ignore this)"
Write-Host "CustomerCounts: $CustomerCounts"
Write-Host "Concurrency:    $Concurrency"
Write-Host "Warmups:        $Warmups"
Write-Host "Runs:            $Runs"
Write-Host "Publish:        $Publish"
Write-Host ''

$failed = @()

foreach ($run in $runsToExecute) {
    $script = Join-Path $root "$run/run-agent-benchmark.ps1"

    if (-not (Test-Path -LiteralPath $script)) {
        Write-Warning "Benchmark runner not found for $run`: $script"
        $failed += $run
        continue
    }

    Write-Host ''
    Write-Host '------------------------------------------------------------' -ForegroundColor DarkGray
    Write-Host " RUNNING $run" -ForegroundColor Yellow
    Write-Host '------------------------------------------------------------' -ForegroundColor DarkGray

    $runArgs = @{
        CustomerCounts = $CustomerCounts
        Concurrency    = $Concurrency
        Warmups        = $Warmups
        Runs           = $Runs
        Publish        = $Publish
    }
    if ($run -in $modeCapableRuns) {
        $runArgs['Mode'] = $Mode
    }
    # KeepDatabase/SkipDockerBuild only exist on the Run1/2/3 wrapper scripts.
    if ($run -in $modeCapableRuns) {
        $runArgs['KeepDatabase'] = [bool]$KeepDatabase
        $runArgs['SkipDockerBuild'] = [bool]$SkipDockerBuild
    }

    try {
        & $script @runArgs

        if ($LASTEXITCODE -ne 0) {
            throw "Benchmark runner exited with code $LASTEXITCODE."
        }

        Write-Host "$run completed." -ForegroundColor Green
    }
    catch {
        Write-Warning "$run FAILED: $($_.Exception.Message)"
        $failed += $run

        # Continue so the remaining benchmark suites can still produce
        # their JSON artifacts.
        continue
    }
}

if ($IncludeSupplyChain) {
    Write-Host ''
    Write-Host '------------------------------------------------------------' -ForegroundColor DarkGray
    Write-Host ' RUNNING SupplyChain E2E (agent authorization workload)' -ForegroundColor Yellow
    Write-Host ' Reports a MODELED token/agent-work reduction estimate -' -ForegroundColor DarkGray
    Write-Host ' there is no conventional flow to compare against here.' -ForegroundColor DarkGray
    Write-Host '------------------------------------------------------------' -ForegroundColor DarkGray

    $supplyChainScript = Join-Path $root 'SupplyChain/run-supply-chain.ps1'
    if (-not (Test-Path -LiteralPath $supplyChainScript)) {
        Write-Warning "SupplyChain runner not found: $supplyChainScript"
        $failed += 'SupplyChain'
    }
    else {
        try {
            & $supplyChainScript
            if ($LASTEXITCODE -ne 0) { throw "SupplyChain runner exited with code $LASTEXITCODE." }
            Write-Host 'SupplyChain E2E completed.' -ForegroundColor Green
        }
        catch {
            Write-Warning "SupplyChain E2E FAILED: $($_.Exception.Message)"
            $failed += 'SupplyChain'
        }
    }
}

if ($IncludeSemanticPipeline) {
    Write-Host ''
    Write-Host '------------------------------------------------------------' -ForegroundColor DarkGray
    Write-Host ' RUNNING SupplyChain.Semantic pipeline-weight benchmark' -ForegroundColor Yellow
    Write-Host ' Reports a MODELED token/agent-work reduction estimate -' -ForegroundColor DarkGray
    Write-Host ' there is no conventional flow to compare against here.' -ForegroundColor DarkGray
    Write-Host '------------------------------------------------------------' -ForegroundColor DarkGray

    $semanticPipelineDir = Join-Path $repoRootForSemantic 'src/csharp/samples/Foundgine.SupplyChain.Advanced/Semantic'
    if (-not (Test-Path -LiteralPath $semanticPipelineDir)) {
        Write-Warning "SupplyChain.Semantic sample not found: $semanticPipelineDir"
        $failed += 'SemanticPipeline'
    }
    else {
        Push-Location $semanticPipelineDir
        try {
            dotnet run -c Release --project Benchmarks
            if ($LASTEXITCODE -ne 0) { throw "dotnet run exited with code $LASTEXITCODE." }
            Write-Host 'SupplyChain.Semantic pipeline-weight benchmark completed.' -ForegroundColor Green
        }
        catch {
            Write-Warning "SupplyChain.Semantic pipeline-weight benchmark FAILED: $($_.Exception.Message)"
            $failed += 'SemanticPipeline'
        }
        finally {
            Pop-Location
        }
    }
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' VERIFYING BENCHMARK ARTIFACTS' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

$standardRuns = @(
    [PSCustomObject]@{ Run = 'Run1'; ArtifactRoot = Join-Path $root 'Run1/artifacts' }
    [PSCustomObject]@{ Run = 'Run2'; ArtifactRoot = Join-Path $root 'Run2/artifacts' }
    [PSCustomObject]@{ Run = 'Run3'; ArtifactRoot = Join-Path $root 'Run3/artifacts' }
    [PSCustomObject]@{ Run = 'Run4'; ArtifactRoot = Join-Path $root 'Run4/artifacts' }
    [PSCustomObject]@{ Run = 'Run5'; ArtifactRoot = Join-Path $root 'Run5/artifacts' }
    [PSCustomObject]@{ Run = 'Run5SameClient'; ArtifactRoot = Join-Path $root 'Run5SameClient/artifacts' }
)
if ($IncludeSupplyChain) {
    $standardRuns += [PSCustomObject]@{ Run = 'SupplyChain (modeled estimate)'; ArtifactRoot = Join-Path $root 'SupplyChain/reports' }
}
if ($IncludeSemanticPipeline) {
    $standardRuns += [PSCustomObject]@{ Run = 'SemanticPipeline (modeled estimate)'; ArtifactRoot = Join-Path $repoRootForSemantic 'src/csharp/samples/Foundgine.SupplyChain.Advanced/Semantic/Benchmarks/reports' }
}

$artifactSummary = foreach ($entry in $standardRuns) {
    $artifactRoot = $entry.ArtifactRoot

    if (-not (Test-Path -LiteralPath $artifactRoot)) {
        [PSCustomObject]@{
            Run       = $entry.Run
            Artifacts = 0
            JSON      = 0
            Status    = 'MISSING'
        }
        continue
    }

    $allFiles = @(Get-ChildItem $artifactRoot -Recurse -File -ErrorAction SilentlyContinue)

    $jsonFiles = @(
        $allFiles |
            Where-Object {
                $_.Extension -eq '.json'
            }
    )

    [PSCustomObject]@{
        Run       = $entry.Run
        Artifacts = $allFiles.Count
        JSON      = $jsonFiles.Count
        Status    = if ($jsonFiles.Count -gt 0) { 'OK' } else { 'NO JSON' }
    }
}

$artifactSummary | Format-Table -AutoSize

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' PUBLISHING AVAILABLE REPORTS' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

$publishFailed = $false

if ($Smoke) {
    Write-Host 'Skipped: -Smoke never publishes to docs-site/assets.' -ForegroundColor DarkGray
}
elseif ($Publish) {
    $publishScript = Join-Path $root 'publish-all-reports.ps1'

    if (-not (Test-Path -LiteralPath $publishScript)) {
        throw "Publish script not found: $publishScript"
    }

    try {
        & $publishScript

        if ($LASTEXITCODE -ne 0) {
            throw "publish-all-reports.ps1 exited with code $LASTEXITCODE."
        }
    }
    catch {
        Write-Warning "Report publication failed: $($_.Exception.Message)"
        $publishFailed = $true
    }
}

# Cleanup. Two cases:
#  - Smoke run: nothing was published, so the local artifacts are the only
#    copy of this data and it's throwaway by definition - delete it so the
#    smoke test leaves the working tree exactly as it found it.
#  - Normal run that published successfully: the data now lives under
#    docs-site/assets/agent-benchmark, so the raw per-run artifacts folders
#    are redundant working files - clean them up too. If publishing failed
#    or was skipped, keep the raw artifacts around so nothing is lost.
$shouldCleanup = $Smoke -or ($Publish -and -not $publishFailed)
if ($shouldCleanup) {
    Write-Host ''
    Write-Host 'Cleaning up local run artifacts...' -ForegroundColor DarkGray
    foreach ($run in $runsToExecute) {
        $artifactRoot = Join-Path $root "$run/artifacts"
        if (Test-Path -LiteralPath $artifactRoot) {
            Remove-Item -LiteralPath $artifactRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan

if ($failed.Count -eq 0) {
    Write-Host ' ALL BENCHMARKS COMPLETED' -ForegroundColor Green
}
else {
    Write-Host " BENCHMARKS WITH FAILURES: $($failed -join ', ')" -ForegroundColor Red
    Write-Host ''
    Write-Host 'The suite continued so successful runs could still produce JSON artifacts.' -ForegroundColor Yellow
}

Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''

if ($failed.Count -gt 0) {
    exit 1
}

exit 0