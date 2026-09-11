[CmdletBinding()]
param(
    [string]$Destination
)

$ErrorActionPreference = 'Stop'

foreach ($run in @(
    'Run1',
    'Run2',
    'Run3',
    'Run4',
    'Run5',
    'Run5SameClient'
)) {
    $script = Join-Path $PSScriptRoot "$run\publish-report.ps1"

    if (-not (Test-Path -LiteralPath $script -PathType Leaf)) {
        throw "Publish script not found for $run`: $script"
    }

    try {
        & $script -Destination $Destination

        if (-not $?) {
            throw "Publish script returned failure for $run."
        }
    }
    catch {
        throw "Failed to publish $run. $($_.Exception.Message)"
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

$matrixDestination = Join-Path `
    $repoRoot `
    'docs-site\assets\agent-benchmark\benchmark-matrix.json'

& (Join-Path $PSScriptRoot 'build-benchmark-matrix.ps1') `
    -Destination $matrixDestination

if (-not $?) {
    throw 'Failed to build benchmark matrix.'
}

& (Join-Path $PSScriptRoot 'validate-published-benchmarks.ps1')
if (-not $?) {
    throw 'Published benchmark validation failed.'
}

Write-Host ''
Write-Host 'All benchmark reports and the interactive benchmark matrix are published.' `
    -ForegroundColor Green
