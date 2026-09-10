$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$reportDir = if ($env:SUPPLY_CHAIN_SEMANTIC_REPORT_DIRECTORY) { $env:SUPPLY_CHAIN_SEMANTIC_REPORT_DIRECTORY } else { Join-Path $root "reports" }
$siteRoot = Join-Path $root "../../../docs-site"
$assetDir = Join-Path $siteRoot "assets/agent-benchmark/semantic-pipeline"

$json = Join-Path $reportDir "pipeline-benchmark.json"

if (-not (Test-Path $json)) { throw "Pipeline-weight JSON report not found: $json. Run 'dotnet run -c Release --project Benchmarks' first." }

New-Item -ItemType Directory -Force -Path $assetDir | Out-Null
Copy-Item $json (Join-Path $assetDir "pipeline-benchmark.json") -Force

$report = Get-Content $json -Raw | ConvertFrom-Json
$manifest = [ordered]@{
    schemaVersion = 1
    publishedUtc = [DateTimeOffset]::UtcNow.ToString("o")
    sourceJson = "reports/pipeline-benchmark.json"
    iterations = $report.iterations
    warmup = $report.warmup
    fullPipelineAvgMicros = $report.fullPipeline.AvgMicros
    fullPipelineAvgKb = $report.fullPipeline.avgKb
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $assetDir "publish-manifest.json") -Encoding utf8

Write-Host "Published SupplyChain.Semantic pipeline-weight report to:" -ForegroundColor Green
Write-Host "  $assetDir"
Write-Host "  pipeline-benchmark.json"
Write-Host "  publish-manifest.json"
