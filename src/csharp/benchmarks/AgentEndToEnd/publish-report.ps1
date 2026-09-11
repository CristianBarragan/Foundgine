[CmdletBinding()]
param(
    [string]$Report,
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$BenchmarkRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $BenchmarkRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($Report)) {
    $Report = Join-Path $BenchmarkRoot 'artifacts\agent-benchmark\agent-benchmark.json'
} elseif (-not [System.IO.Path]::IsPathRooted($Report)) {
    $Report = Join-Path (Get-Location) $Report
}

if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $RepoRoot 'docs-site\assets\agent-benchmark.json'
} elseif (-not [System.IO.Path]::IsPathRooted($Destination)) {
    $Destination = Join-Path (Get-Location) $Destination
}

if (-not (Test-Path -LiteralPath $Report -PathType Leaf)) {
    throw "Benchmark report not found: $Report`nRun .\benchmarks\AgentEndToEnd\run-agent-benchmark.ps1 first."
}

$destinationDirectory = Split-Path -Parent $Destination
New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
Copy-Item -Force -LiteralPath $Report -Destination $Destination

Write-Host "Published benchmark report:" -ForegroundColor Green
Write-Host "  $Destination"
