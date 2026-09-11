[CmdletBinding()]
param(
    [string]$Run5bRoot,
    [string]$Destination
)

# Run5SameClient writes one raw sample per concurrent task and does not persist
# the per-iteration RunSummary objects. Sample ordering is deterministic:
# Measure() appends exactly `concurrency` task samples for each measured run,
# then the metadata serializer emits all samples for each implementation.
# Therefore each implementation's ordered sample list can be chunked by
# `concurrency` to reconstruct every measured iteration exactly.
#
# RPS is logical operations completed / slowest successful worker wall time
# for that iteration, matching Runner/Program.cs. Never average per-task
# instantaneous RPS; that overstates throughput under concurrent stragglers.

$ErrorActionPreference = 'Stop'
$BenchmarkRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $BenchmarkRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($Run5bRoot)) { $Run5bRoot = Join-Path $BenchmarkRoot 'Run5SameClient\artifacts' }
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path $RepoRoot 'docs-site\assets\agent-benchmark\run5b-aggregate.json' }

if (-not (Test-Path -LiteralPath $Run5bRoot -PathType Container)) { throw "Run 5b artifact directory not found: $Run5bRoot" }
$metadataFiles = @(Get-ChildItem -LiteralPath $Run5bRoot -Recurse -Filter 'run5-same-client-metadata.json' -File)
if ($metadataFiles.Count -eq 0) { throw "No run5-same-client-metadata.json files found under: $Run5bRoot" }

function Get-CanonicalImplementation([string]$raw) {
    if ($raw -match 'Foundgine') { return 'MCP + Foundgine' }
    return 'MCP + EF Core'
}
function Get-Percentile([double[]]$values, [double]$p) {
    if ($values.Count -eq 0) { return 0 }
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 1) { return $sorted[0] }
    $position = ($sorted.Count - 1) * $p
    $lower = [math]::Floor($position); $upper = [math]::Ceiling($position)
    if ($lower -eq $upper) { return $sorted[$lower] }
    return $sorted[$lower] + (($sorted[$upper] - $sorted[$lower]) * ($position - $lower))
}

$aggregate = @()
foreach ($file in $metadataFiles) {
    $doc = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $customers = [int]$doc.customers
    $concurrency = [int]$doc.concurrency
    $batchSize = [int]$doc.batchSize

    # Exclude the 1x1 smoke fixture from the published 4x4 grid.
    if ($customers -notin @(10,100,1000,10000) -or $concurrency -notin @(8,16,32,64)) { continue }

    foreach ($implGroup in (@($doc.samples) | Group-Object implementation)) {
        $samples = @($implGroup.Group)
        $impl = Get-CanonicalImplementation $samples[0].implementation
        if (($samples.Count % $concurrency) -ne 0) {
            throw "Sample count $($samples.Count) is not divisible by concurrency $concurrency in $($file.FullName) for $impl"
        }

        $runRps = @()
        for ($offset = 0; $offset -lt $samples.Count; $offset += $concurrency) {
            $iteration = @($samples[$offset..($offset + $concurrency - 1)])
            $ok = @($iteration | Where-Object { [bool]$_.Success })
            $maxWall = if ($ok.Count -gt 0) { ($ok | Measure-Object -Property WallMs -Maximum).Maximum } else { 0 }
            $logicalOps = if ($ok.Count -gt 0) { ($ok | Measure-Object -Property LogicalOps -Sum).Sum } else { 0 }
            $runRps += if ($maxWall -gt 0) { [double]$logicalOps * 1000.0 / [double]$maxWall } else { 0 }
        }

        $okAll = @($samples | Where-Object { [bool]$_.Success })
        $walls = @($okAll | ForEach-Object { [double]$_.WallMs })
        $avgToolCalls = ($okAll | Measure-Object -Property ToolCalls -Average).Average
        $tokenCallDivisor = if ($avgToolCalls -gt 0) { $avgToolCalls } else { 1 }
        # Bytes/4 is the benchmark token heuristic. Store tokens PER TOOL CALL
        # because the website normalizes per logical operation via
        # logicalOps/toolCalls exactly once.
        $inputTokens = (($okAll | Measure-Object -Property InputBytes -Average).Average / 4.0) / $tokenCallDivisor
        $outputTokens = (($okAll | Measure-Object -Property OutputBytes -Average).Average / 4.0) / $tokenCallDivisor

        $aggregate += [pscustomobject]@{
            customers              = $customers
            concurrency            = $concurrency
            batchSize              = $batchSize
            implementation         = $impl
            samples                = [int]$doc.runs
            rps                    = ($runRps | Measure-Object -Average).Average
            avgWallMs              = ($walls | Measure-Object -Average).Average
            p50Ms                  = Get-Percentile $walls 0.50
            p95Ms                  = Get-Percentile $walls 0.95
            p99Ms                  = Get-Percentile $walls 0.99
            success                = ($okAll | Measure-Object).Count
            failed                 = $samples.Count - $okAll.Count
            toolCalls              = ($okAll | Measure-Object -Property ToolCalls -Average).Average
            logicalOps             = ($okAll | Measure-Object -Property LogicalOps -Average).Average
            estimatedInputTokens   = $inputTokens
            estimatedOutputTokens  = $outputTokens
            estimatedContextTokens = $inputTokens + $outputTokens
        }
    }
}

$result = [ordered]@{
    run       = 'Run5SameClient'
    aggregate = @($aggregate | Sort-Object customers, concurrency, implementation)
}
$dir = Split-Path -Parent $Destination
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Destination -Encoding UTF8
Write-Host "Built Run5SameClient aggregate: $Destination ($($aggregate.Count) rows)" -ForegroundColor Green
