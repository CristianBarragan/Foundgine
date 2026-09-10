[CmdletBinding()]
param(
    [string]$Run2Root,
    [string]$Destination
)

# NOTE: run2-aggregate.json had no build script behind it (unlike Run 5 -
# see build-run5-aggregate.ps1) and was apparently hand-maintained at some
# point. That copy never included an `rps` field, so the site's "RPS ratio"
# matrix metric (docs-site/agent-benchmark/index.html, r.rps) renders every
# cell as blank/"Not measured" even for tiers that were actually measured.
# This script derives the aggregate directly from the raw per-cell
# agent-benchmark.json files every time, so it can never drift out of sync
# or silently drop a field again.

$ErrorActionPreference = 'Stop'
$BenchmarkRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $BenchmarkRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($Run2Root)) { $Run2Root = Join-Path $BenchmarkRoot 'Run2\artifacts' }
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path $RepoRoot 'docs-site\assets\agent-benchmark\run2-aggregate.json' }

if (-not (Test-Path -LiteralPath $Run2Root -PathType Container)) { throw "Run 2 artifact directory not found: $Run2Root" }

# Tier directories are named e.g. "00010-customers"; the raw report itself
# has no customer-count field, only Concurrency, so the count has to come
# from the folder name.
$reportFiles = @(Get-ChildItem -LiteralPath $Run2Root -Recurse -Filter 'agent-benchmark.json' -File)
if ($reportFiles.Count -eq 0) { throw "No agent-benchmark.json files found under: $Run2Root" }

function Get-CustomerCountFromPath([string]$path) {
    $match = [regex]::Match($path, '(\d+)-customers')
    if (-not $match.Success) { throw "Could not determine customer count from path: $path" }
    return [int]$match.Groups[1].Value
}

$rows = @()
foreach ($file in $reportFiles) {
    $doc = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $customers = Get-CustomerCountFromPath $file.FullName
    $concurrency = [int]$doc.Concurrency

    # Run2's Comparison.WallClockMs is a flat average of individual worker
    # latency. Throughput must instead use the batch completion time for each
    # measured iteration: the MAX WallClockMs across its concurrent workers.
    $maxByFlow = @{ 'Conventional' = @(); 'Foundgine' = @() }
    foreach ($group in ($doc.Results | Group-Object Run, Flow)) {
        $items = @($group.Group)
        $flow = [string]$items[0].Flow
        $maxByFlow[$flow] += ($items | Measure-Object -Property WallClockMs -Maximum).Maximum
    }

    foreach ($implKey in @('Conventional', 'Foundgine')) {
        $c = $doc.Comparison.$implKey
        if ($null -eq $c) { continue }

        $maxes = $maxByFlow[$implKey]
        $avgMaxWall = if ($maxes.Count -gt 0) { ($maxes | Measure-Object -Average).Average } else { 0 }
        $rps = if ($avgMaxWall -gt 0) { $concurrency * 1000.0 / $avgMaxWall } else { 0 }

        $rows += [pscustomobject]@{
            customers              = $customers
            concurrency            = $concurrency
            implementation         = $implKey
            rps                    = $rps
            avgWallMs              = [double]$c.WallClockMs
            p50Ms                  = [double]$c.P50WallClockMs
            p95Ms                  = [double]$c.P95WallClockMs
            p99Ms                  = [double]$c.P99WallClockMs
            toolCalls              = [double]$c.ToolCalls
            estimatedContextTokens = [double]$c.EstimatedContextLoadTokens
            successRate            = [double]$c.SuccessRate
        }
    }
}

$aggregate = @($rows | Sort-Object customers, concurrency, implementation)

$result = [ordered]@{
    run       = 'Run2'
    aggregate = $aggregate
}

$dir = Split-Path -Parent $Destination
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Destination -Encoding UTF8
Write-Host "Built Run2 aggregate: $Destination" -ForegroundColor Green
Write-Host "  Aggregate rows: $($aggregate.Count)"
Write-Host "  (Full 4x4 grid is 16 cells x 2 implementations = 32 rows; fewer rows just means fewer tiers have been run yet.)"
