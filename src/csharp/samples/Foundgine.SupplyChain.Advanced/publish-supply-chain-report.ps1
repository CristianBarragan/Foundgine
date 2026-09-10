$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$reportDir = if ($env:SUPPLY_CHAIN_REPORT_DIRECTORY) { $env:SUPPLY_CHAIN_REPORT_DIRECTORY } else { Join-Path $root "reports" }
$siteRoot = Join-Path $root "../../docs-site"
$assetDir = Join-Path $siteRoot "assets/agent-benchmark/supply-chain"

$json = Join-Path $reportDir "supply-chain-report.json"
$markdown = Join-Path $reportDir "supply-chain-report.md"

if (-not (Test-Path $json)) { throw "Supply Chain JSON report not found: $json. Run run-supply-chain.ps1 first." }
if (-not (Test-Path $markdown)) { throw "Supply Chain Markdown report not found: $markdown. Run run-supply-chain.ps1 first." }

New-Item -ItemType Directory -Force -Path $assetDir | Out-Null
Copy-Item $json (Join-Path $assetDir "supply-chain-report.json") -Force
Copy-Item $markdown (Join-Path $assetDir "supply-chain-report.md") -Force

$report = Get-Content $json -Raw | ConvertFrom-Json
$manifest = [ordered]@{
    schemaVersion = 1
    publishedUtc = [DateTimeOffset]::UtcNow.ToString("o")
    sourceJson = "reports/supply-chain-report.json"
    sourceMarkdown = "reports/supply-chain-report.md"
    seed = $report.seed
    steps = $report.steps
    customers = $report.customers
    success = $report.summary.success
    failures = $report.summary.failures
    # See the matching comment in merge-supply-chain-pentest-report.ps1: the
    # Agent writes this field as "unexpectedUnauthorizedSuccesses", not
    # "unexpectedSuccesses".
    unexpectedUnauthorizedSuccesses = $report.summary.unexpectedUnauthorizedSuccesses
    averageLatencyMs = $report.summary.avgLatencyMs
    securityPenTest = if ($report.securityPenTest) {
        [ordered]@{
            total = $report.securityPenTest.summary.total
            passed = $report.securityPenTest.summary.passed
            failed = $report.securityPenTest.summary.failed
            skipped = $report.securityPenTest.summary.skipped
            suiteDurationMs = $report.securityPenTest.suiteDurationMs
            averageCaseDurationMs = $report.securityPenTest.summary.averageCaseDurationMs
        }
    } else { $null }
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $assetDir "publish-manifest.json") -Encoding utf8

Write-Host "Published Supply Chain E2E report to:" -ForegroundColor Green
Write-Host "  $assetDir"
Write-Host "  supply-chain-report.json"
Write-Host "  supply-chain-report.md"
Write-Host "  publish-manifest.json"
