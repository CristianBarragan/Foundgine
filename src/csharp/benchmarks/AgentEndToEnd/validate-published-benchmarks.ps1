[CmdletBinding()]
param([string]$AssetsRoot)

$ErrorActionPreference = 'Stop'
$BenchmarkRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $BenchmarkRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($AssetsRoot)) {
    $AssetsRoot = Join-Path $RepoRoot 'docs-site\assets\agent-benchmark'
}

function Read-Aggregate([string]$name) {
    $path = Join-Path $AssetsRoot $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing aggregate: $path" }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

$expected = @{
    'run1-aggregate.json' = 8
    'run2-aggregate.json' = 14
    'run3-aggregate.json' = 8
    'run4-aggregate.json' = 32
    'run5-aggregate.json' = 32
    'run5b-aggregate.json' = 32
}

foreach ($entry in $expected.GetEnumerator()) {
    $doc = Read-Aggregate $entry.Key
    $count = @($doc.aggregate).Count
    if ($count -ne $entry.Value) { throw "$($entry.Key): expected $($entry.Value) rows, found $count." }
}

function Assert-FullPairs($doc, [string]$name) {
    $pairs = @($doc.aggregate | Group-Object customers,concurrency)
    if ($pairs.Count -ne 16) { throw "${name}: expected 16 workload/concurrency cells, found $($pairs.Count)." }
    foreach ($pair in $pairs) {
        if (@($pair.Group).Count -ne 2) { throw "${name}: cell '$($pair.Name)' does not contain exactly two implementations." }
    }
}

function Assert-FoundgineWins($doc, [string]$name) {
    $ratios = @()
    foreach ($pair in @($doc.aggregate | Group-Object customers,concurrency)) {
        $c = @($pair.Group | Where-Object { $_.implementation -in @('Conventional','MCP + EF Core') })[0]
        $f = @($pair.Group | Where-Object { $_.implementation -match 'Foundgine' })[0]
        if ($null -eq $c -or $null -eq $f -or [double]$c.rps -le 0) { throw "${name}: invalid RPS pair '$($pair.Name)'." }
        $ratios += [double]$f.rps / [double]$c.rps
    }
    if (($ratios | Measure-Object -Minimum).Minimum -le 1) { throw "${name}: Foundgine is not above conventional in every published cell." }
    Write-Host "${name}: Foundgine RPS ratio range $([math]::Round(($ratios | Measure-Object -Minimum).Minimum,3))x - $([math]::Round(($ratios | Measure-Object -Maximum).Maximum,3))x; mean $([math]::Round(($ratios | Measure-Object -Average).Average,3))x" -ForegroundColor Green
}

$run4 = Read-Aggregate 'run4-aggregate.json'
$run5 = Read-Aggregate 'run5-aggregate.json'
$run5b = Read-Aggregate 'run5b-aggregate.json'
Assert-FullPairs $run4 'Run4'
Assert-FullPairs $run5 'Run5'
Assert-FullPairs $run5b 'Run5b'
Assert-FoundgineWins $run4 'Run4'
Assert-FoundgineWins $run5 'Run5'
Assert-FoundgineWins $run5b 'Run5b'

if (@($run5b.aggregate | Where-Object { [int]$_.failed -ne 0 }).Count -ne 0) { throw 'Run5b: published aggregate contains failures.' }

$index = Join-Path $RepoRoot 'docs-site\agent-benchmark\index.html'
$indexText = Get-Content -LiteralPath $index -Raw
if ($indexText -notmatch 'run-4/index\.html') { throw 'Benchmark landing page does not link to Run 4.' }
if ($indexText -notmatch 'aggregateFile') { throw 'Benchmark landing page does not use the explicit aggregate-file mapping.' }
$runsJson = Get-Content -LiteralPath (Join-Path $RepoRoot 'docs-site\assets\agent-benchmark\runs.json') -Raw | ConvertFrom-Json
$run4Manifest = @($runsJson.runs | Where-Object { $_.id -eq '4' })[0]
$run5bManifest = @($runsJson.runs | Where-Object { $_.id -eq '5b' })[0]
if ($null -eq $run4Manifest -or $run4Manifest.aggregateFile -ne 'run4-aggregate.json') { throw 'runs.json does not map Run 4 to run4-aggregate.json.' }
if ($null -eq $run5bManifest -or $run5bManifest.aggregateFile -ne 'run5b-aggregate.json') { throw 'runs.json does not map Run 5b to run5b-aggregate.json.' }
$matrixText = Get-Content -LiteralPath (Join-Path $RepoRoot 'docs-site\assets\agent-benchmark\benchmark-matrix.json') -Raw
if ($matrixText -notmatch '"run"\s*:\s*"Run4"') { throw 'Benchmark matrix does not contain Run 4 rows.' }
if ($matrixText -notmatch '"run"\s*:\s*"Run5SameClient"') { throw 'Benchmark matrix does not contain Run 5b rows.' }

Write-Host 'Published benchmark validation: PASS' -ForegroundColor Green
