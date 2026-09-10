[CmdletBinding()]
param(
    [string]$Run3Root,
    [string]$Destination
)

# Mirrors build-run2-aggregate.ps1: Run3's raw agent-benchmark.json reports
# have no dedicated aggregate file behind the published matrix, so this
# derives it directly from the raw per-cell reports every time.
#
# Run3 only ever measured 4 of the 16 (customers x concurrency) cells - the
# 10-customer tier at C8, and the 100/1,000/10,000-customer tiers at C64.
# That is real, not a bug: this script does not invent the missing cells.

$ErrorActionPreference = 'Stop'
$BenchmarkRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $BenchmarkRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($Run3Root)) { $Run3Root = Join-Path $BenchmarkRoot 'Run3\artifacts' }
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path $RepoRoot 'docs-site\assets\agent-benchmark\run3-aggregate.json' }

if (-not (Test-Path -LiteralPath $Run3Root -PathType Container)) { throw "Run 3 artifact directory not found: $Run3Root" }

$reportFiles = @(Get-ChildItem -LiteralPath $Run3Root -Recurse -Filter 'agent-benchmark.json' -File)
if ($reportFiles.Count -eq 0) { throw "No agent-benchmark.json files found under: $Run3Root" }

function Get-CustomerCountFromPath([string]$path) {
    $match = [regex]::Match($path, '(\d+)-customers')
    if (-not $match.Success) { throw "Could not determine customer count from path: $path" }
    return [int]$match.Groups[1].Value
}

$rows = @()
foreach ($file in $reportFiles) {
    $doc = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $customers = Get-CustomerCountFromPath $file.FullName
    $concurrency = [int]$doc.Configuration.Concurrency

    foreach ($implKey in @('Conventional', 'Foundgine')) {
        $c = $doc.Comparison.$implKey
        if ($null -eq $c) { continue }

        $wall = [double]$c.WallClockMs
        $rps = if ($wall -gt 0) { $concurrency * 1000.0 / $wall } else { 0 }

        $rows += [pscustomobject]@{
            customers              = $customers
            concurrency            = $concurrency
            implementation         = $implKey
            rps                    = $rps
            avgWallMs              = $wall
            toolCalls              = [double]$c.ToolCalls
            estimatedContextTokens = [double]$c.EstimatedContextLoadTokens
        }
    }
}

$aggregate = @($rows | Sort-Object customers, concurrency, implementation)
$result = [ordered]@{ run = 'Run3'; aggregate = $aggregate }

$dir = Split-Path -Parent $Destination
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Destination -Encoding UTF8
Write-Host "Built Run3 aggregate: $Destination ($($aggregate.Count) rows)" -ForegroundColor Green
