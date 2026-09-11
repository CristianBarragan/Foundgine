[CmdletBinding()]
param(
    [string]$Run5Root,
    [string]$Destination
)

# NOTE: run5-aggregate.json was previously hand-maintained and never
# regenerated after the Run5 raw metadata was corrected (commit d1e3f60,
# "correction") to properly scale success/rps for the Foundgine UNNEST
# batch path (batchSize items per concurrent call). That left the
# published matrix + narrative reading stale, non-batch-scaled numbers
# for the Foundgine path (e.g. showing a "scalability cliff" at C64 that
# does not exist in the raw data). This script derives the aggregate
# directly from the raw per-cell run5-metadata.json files every time, so
# it can never drift out of sync with the raw benchmark output again.

$ErrorActionPreference = 'Stop'
$BenchmarkRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $BenchmarkRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($Run5Root)) { $Run5Root = Join-Path $BenchmarkRoot 'Run5\artifacts' }
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path $RepoRoot 'docs-site\assets\agent-benchmark\run5-aggregate.json' }

if (-not (Test-Path -LiteralPath $Run5Root -PathType Container)) { throw "Run 5 artifact directory not found: $Run5Root" }

$metadataFiles = @(Get-ChildItem -LiteralPath $Run5Root -Recurse -Filter 'run5-metadata.json' -File)
if ($metadataFiles.Count -eq 0) { throw "No run5-metadata.json files found under: $Run5Root" }

function Get-CanonicalImplementation([string]$raw) {
    # Canonical names must match the regex the matrix JS relies on
    # (docs-site/agent-benchmark/index.html, function pair()):
    #   conventional: /^(?:Conventional|MCP \+ EF Core)$/
    #   foundgine:    /^MCP \+ Foundgine Postgres$|^Foundgine$/
    if ($raw -match 'Foundgine') { return 'MCP + Foundgine Postgres' }
    return 'MCP + EF Core'
}

$raw = @()
foreach ($file in $metadataFiles) {
    $doc = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $customers = [int]$doc.customers
    $concurrency = [int]$doc.concurrency
    $batchSize = [int]$doc.batchSize
    foreach ($sample in @($doc.samples)) {
        $impl = Get-CanonicalImplementation $sample.implementation
        # Estimated token fields are measured per MCP call. Preserve that
        # unit in the aggregate and expose logicalOps so the website can
        # normalize exactly once to per-operation context when comparing a
        # 1-op EF call with an 8-op Foundgine batch call.
        $raw += [pscustomobject]@{
            customers      = $customers
            concurrency    = $concurrency
            implementation = $impl
            rps            = [double]$sample.Rps
            avgWallMs      = [double]$sample.AvgWallMs
            p50Ms          = [double]$sample.P50Ms
            p95Ms          = [double]$sample.P95Ms
            p99Ms          = [double]$sample.P99Ms
            # Success/Failed are already batch-scaled by the Run5 runner
            # (Runner/Program.cs: ok.Length * batchSize) - sum, don't recompute.
            success        = [double]$sample.Success
            failed         = [double]$sample.Failed
            toolCalls      = [double]$sample.ToolCalls
            logicalOps       = if ($impl -eq 'MCP + Foundgine Postgres') { $batchSize } else { 1 }
            batchSize        = $batchSize
            estimatedInputTokens  = [double]$sample.EstimatedInputTokens
            estimatedOutputTokens = [double]$sample.EstimatedOutputTokens
        }
    }
}

$aggregate = @()
foreach ($group in ($raw | Group-Object customers, concurrency, implementation)) {
    $items = @($group.Group)
    $first = $items[0]
    $avg = { param($name) (($items | Measure-Object -Property $name -Average).Average) }
    $inputTok = & $avg 'estimatedInputTokens'
    $outputTok = & $avg 'estimatedOutputTokens'
    $aggregate += [pscustomobject]@{
        customers              = $first.customers
        concurrency             = $first.concurrency
        implementation          = $first.implementation
        rps                     = & $avg 'rps'
        avgWallMs               = & $avg 'avgWallMs'
        p50Ms                   = & $avg 'p50Ms'
        p95Ms                   = & $avg 'p95Ms'
        p99Ms                   = & $avg 'p99Ms'
        success                 = (($items | Measure-Object -Property success -Sum).Sum)
        failed                  = (($items | Measure-Object -Property failed -Sum).Sum)
        toolCalls               = & $avg 'toolCalls'
        logicalOps              = & $avg 'logicalOps'
        batchSize               = [int]$first.batchSize
        estimatedInputTokens    = $inputTok
        estimatedOutputTokens   = $outputTok
        estimatedContextTokens  = $inputTok + $outputTok
    }
}

$result = [ordered]@{
    run       = 'Run5'
    aggregate = @($aggregate | Sort-Object customers, concurrency, implementation)
}

$dir = Split-Path -Parent $Destination
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Destination -Encoding UTF8
Write-Host "Built Run5 aggregate: $Destination" -ForegroundColor Green
Write-Host "  Aggregate rows: $($aggregate.Count)"
Write-Host "  Sample rows:    $($raw.Count)"
