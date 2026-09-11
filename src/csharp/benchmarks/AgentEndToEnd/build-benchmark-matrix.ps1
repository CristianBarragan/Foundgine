[CmdletBinding()]
param([string]$Destination)

$ErrorActionPreference = 'Stop'
$BenchmarkRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $BenchmarkRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $RepoRoot 'docs-site\assets\agent-benchmark\benchmark-matrix.json'
}

# This file is a derived index over the authoritative per-run aggregate files.
# It deliberately does not reinterpret raw benchmark samples. Each run builder
# owns its measurement semantics; this script only combines those already
# validated aggregates into one machine-readable matrix artifact.
$definitions = @(
    [pscustomobject]@{ Name='Run1'; File='run1-aggregate.json'; Variant='standard' }
    [pscustomobject]@{ Name='Run2'; File='run2-aggregate.json'; Variant='standard' }
    [pscustomobject]@{ Name='Run3'; File='run3-aggregate.json'; Variant='standard' }
    [pscustomobject]@{ Name='Run4'; File='run4-aggregate.json'; Variant='standard' }
    [pscustomobject]@{ Name='Run5'; File='run5-aggregate.json'; Variant='high-assurance-batch' }
    [pscustomobject]@{ Name='Run5SameClient'; File='run5b-aggregate.json'; Variant='same-client' }
)

$aggregateRoot = Join-Path $RepoRoot 'docs-site\assets\agent-benchmark'
$includedRuns = @()
$rows = @()
foreach ($definition in $definitions) {
    $path = Join-Path $aggregateRoot $definition.File
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    $doc = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $includedRuns += $definition.Name
    foreach ($row in @($doc.aggregate)) {
        $rows += [pscustomobject]@{
            run = $definition.Name
            variant = $definition.Variant
            customers = if ($null -ne $row.customers) { [int]$row.customers } else { 0 }
            concurrency = if ($null -ne $row.concurrency) { [int]$row.concurrency } else { 0 }
            implementation = [string]$row.implementation
            rps = if ($null -ne $row.rps) { [double]$row.rps } elseif ($null -ne $row.avgRps) { [double]$row.avgRps } else { 0 }
            avgWallMs = if ($null -ne $row.avgWallMs) { [double]$row.avgWallMs } else { 0 }
            p50Ms = if ($null -ne $row.p50Ms) { [double]$row.p50Ms } else { 0 }
            p95Ms = if ($null -ne $row.p95Ms) { [double]$row.p95Ms } else { 0 }
            p99Ms = if ($null -ne $row.p99Ms) { [double]$row.p99Ms } else { 0 }
            toolCalls = if ($null -ne $row.toolCalls) { [double]$row.toolCalls } else { 0 }
            logicalOps = if ($null -ne $row.logicalOps) { [double]$row.logicalOps } else { 0 }
            estimatedContextTokens = if ($null -ne $row.estimatedContextTokens) { [double]$row.estimatedContextTokens } else { 0 }
            success = if ($null -ne $row.success) { [int]$row.success } else { 0 }
            failed = if ($null -ne $row.failed) { [int]$row.failed } else { 0 }
        }
    }
}
if ($rows.Count -eq 0) { throw 'No run aggregate files were found.' }
$result = [ordered]@{
    schemaVersion = 4
    generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    source = 'Per-run benchmark aggregate files'
    runs = @($includedRuns)
    aggregate = @($rows | Sort-Object run,customers,concurrency,implementation)
}
$dir = Split-Path -Parent $Destination
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Destination -Encoding UTF8
Write-Host "Built benchmark matrix: $Destination" -ForegroundColor Green
Write-Host "  Runs:           $($includedRuns -join ', ')"
Write-Host "  Aggregate rows: $($rows.Count)"
