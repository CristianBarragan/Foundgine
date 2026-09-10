[CmdletBinding()]
param(
    [string]$Run4Root,
    [string]$Destination
)

# Run4's raw metadata (run4-metadata.json) already carries a `samples`
# array with per-run rps/avgWallMs/etc. computed by the benchmark runner
# itself, similar to Run5. This averages those samples per
# (customerCount, concurrency, implementation) cell.
#
# IMPORTANT: each file's `samples` array mixes TWO different comparisons
# under each implementation - option "...agent" (the actual agent-driven
# flow this matrix is meant to report) and option "...single request" /
# "...single tool" (a separate raw-baseline measurement). Averaging both
# options together silently blends two different scenarios and produces
# wrong ratios (e.g. reads ~4.8x instead of the correct 4.93x at
# 10 customers/C8). Only the "...agent" option must be used here.

$ErrorActionPreference = 'Stop'
$BenchmarkRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $BenchmarkRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($Run4Root)) { $Run4Root = Join-Path $BenchmarkRoot 'Run4\artifacts' }
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path $RepoRoot 'docs-site\assets\agent-benchmark\run4-aggregate.json' }

if (-not (Test-Path -LiteralPath $Run4Root -PathType Container)) { throw "Run 4 artifact directory not found: $Run4Root" }

$metadataFiles = @(Get-ChildItem -LiteralPath $Run4Root -Recurse -Filter 'run4-metadata.json' -File)
if ($metadataFiles.Count -eq 0) { throw "No run4-metadata.json files found under: $Run4Root" }

$AgentOptions = @{
    'GraphQL+HotChocolate+EF agent' = 'Conventional'
    'MCP+Foundgine agent'           = 'Foundgine'
}

$raw = @()
foreach ($file in $metadataFiles) {
    $doc = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $customers = [int]$doc.customerCount
    $concurrency = [int]$doc.concurrency
    foreach ($sample in @($doc.samples)) {
        if (-not $AgentOptions.ContainsKey($sample.option)) { continue }
        $impl = $AgentOptions[$sample.option]
        $raw += [pscustomobject]@{
            customers              = $customers
            concurrency            = $concurrency
            implementation         = $impl
            rps                    = [double]$sample.rps
            avgWallMs              = [double]$sample.avgWallMs
            p50Ms                  = [double]$sample.p50Ms
            p95Ms                  = [double]$sample.p95Ms
            p99Ms                  = [double]$sample.p99Ms
            toolCalls               = [double]$sample.toolCalls
            estimatedContextTokens  = [double]$sample.estimatedContextTokens
        }
    }
}

$aggregate = @()
foreach ($group in ($raw | Group-Object customers, concurrency, implementation)) {
    $items = @($group.Group)
    $first = $items[0]
    $avg = { param($name) (($items | Measure-Object -Property $name -Average).Average) }
    $aggregate += [pscustomobject]@{
        customers              = $first.customers
        concurrency            = $first.concurrency
        implementation         = $first.implementation
        rps                    = & $avg 'rps'
        avgWallMs              = & $avg 'avgWallMs'
        p50Ms                  = & $avg 'p50Ms'
        p95Ms                  = & $avg 'p95Ms'
        p99Ms                  = & $avg 'p99Ms'
        toolCalls               = & $avg 'toolCalls'
        estimatedContextTokens  = & $avg 'estimatedContextTokens'
    }
}

$aggregate = @($aggregate | Sort-Object customers, concurrency, implementation)
$result = [ordered]@{ run = 'Run4'; aggregate = $aggregate }

$dir = Split-Path -Parent $Destination
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Destination -Encoding UTF8
Write-Host "Built Run4 aggregate: $Destination ($($aggregate.Count) rows)" -ForegroundColor Green
