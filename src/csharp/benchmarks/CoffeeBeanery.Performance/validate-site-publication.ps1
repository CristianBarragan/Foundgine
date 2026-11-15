[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$BenchmarkRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $BenchmarkRoot '../../../../')).Path
$siteRoot = Join-Path $RepoRoot 'docs-site'
$assetRoot = Join-Path $siteRoot 'assets/coffee-beanery-benchmark'
$manifestPath = Join-Path $assetRoot 'reports-index.json'
$pagePath = Join-Path $siteRoot 'performance/index.html'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Missing report index: $manifestPath" }
if (-not (Test-Path -LiteralPath $pagePath -PathType Leaf)) { throw "Missing performance page: $pagePath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (@($manifest.reports | Where-Object { $_.format -eq 'json' }).Count -eq 0) { throw 'No JSON report entries are published.' }
foreach ($entry in $manifest.reports) {
    $path = Join-Path $assetRoot ($entry.path -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Published report missing: $path" }
}
$page = Get-Content -LiteralPath $pagePath -Raw
if ($page -notmatch 'coffee-beanery-benchmark/reports-index\.json') { throw 'Performance page does not load the CoffeeBeanery report index.' }
if ($page -notmatch 'coffeebeanery-performance') { throw 'Performance page has no CoffeeBeanery evidence section.' }
$readme = Get-Content -LiteralPath (Join-Path $BenchmarkRoot 'README.md') -Raw
if ($readme -notmatch 'cristianbarragan\.github\.io/Foundgine/performance/#coffeebeanery-performance') { throw 'CoffeeBeanery README does not link to the public performance evidence.' }
Write-Host "CoffeeBeanery website publication validation: PASS ($(@($manifest.reports).Count) artifacts)" -ForegroundColor Green
