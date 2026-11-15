[CmdletBinding()]
param(
    [string]$ReportRoot,
    [string]$DestinationRoot
)

$ErrorActionPreference = 'Stop'
$BenchmarkRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $BenchmarkRoot '../../../../')).Path
if ([string]::IsNullOrWhiteSpace($ReportRoot)) { $ReportRoot = Join-Path $BenchmarkRoot 'reports' }
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) { $DestinationRoot = Join-Path $RepoRoot 'docs-site/assets/coffee-beanery-benchmark' }
if (-not (Test-Path -LiteralPath $ReportRoot -PathType Container)) { throw "CoffeeBeanery report directory not found: $ReportRoot" }

$files = @(Get-ChildItem -LiteralPath $ReportRoot -Recurse -File | Where-Object { $_.Extension -in @('.json','.csv','.md') } | Sort-Object FullName)
if ($files.Count -eq 0) { throw "No CoffeeBeanery report artifacts found under: $ReportRoot" }

$reportsDestination = Join-Path $DestinationRoot 'reports'
if (Test-Path -LiteralPath $reportsDestination) { Remove-Item -LiteralPath $reportsDestination -Recurse -Force }
New-Item -ItemType Directory -Force -Path $reportsDestination | Out-Null
$entries = @()
foreach ($file in $files) {
    $relative = $file.FullName.Substring((Resolve-Path $ReportRoot).Path.Length).TrimStart('\','/')
    $target = Join-Path $reportsDestination $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    $entry = [ordered]@{
        path = ('reports/' + ($relative -replace '\\','/'))
        name = $file.Name
        format = $file.Extension.TrimStart('.').ToLowerInvariant()
        generatedAt = $null
        target = @()
    }
    if ($file.Extension -eq '.json') {
        try {
            $report = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            if ($report.GeneratedAt) { $entry.generatedAt = $report.GeneratedAt }
            $entry.target = @($report.Results | ForEach-Object { $_.Target } | Where-Object { $_ } | Sort-Object -Unique)
        } catch { throw "Invalid JSON benchmark report: $($file.FullName). $($_.Exception.Message)" }
    }
    $entries += [pscustomobject]$entry
}
$manifest = [ordered]@{
    schemaVersion = 1
    generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    source = 'src/csharp/benchmarks/CoffeeBeanery.Performance/reports'
    note = 'Checked-in benchmark report artifacts. These are historical measurements, not a live feed.'
    reports = $entries
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $DestinationRoot 'reports-index.json') -Encoding UTF8
Write-Host "Published $($files.Count) CoffeeBeanery report artifacts to $DestinationRoot" -ForegroundColor Green
& (Join-Path $BenchmarkRoot 'validate-site-publication.ps1')
if (-not $?) { throw 'CoffeeBeanery website publication validation failed.' }
