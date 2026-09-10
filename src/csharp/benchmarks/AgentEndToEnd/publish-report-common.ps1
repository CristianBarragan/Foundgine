[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Run1','Run2','Run3','Run4','Run5','Run5SameClient')][string]$Run,
    [string]$ReportRoot,
    [string]$DestinationRoot
)
$ErrorActionPreference = 'Stop'
$BenchmarkRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $BenchmarkRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $DestinationRoot = Join-Path $RepoRoot 'docs-site\assets\agent-benchmark'
}
$source = if ([string]::IsNullOrWhiteSpace($ReportRoot)) {
    switch ($Run) {
        # NOTE: Run1's runner (run-agent-benchmark.ps1) writes tier folders
        # directly under 'Run1\artifacts\<tier>\concurrency-XXX\' - there is
        # no 'agent-benchmark' subfolder in that layout. The previous mapping
        # here ('Run1\artifacts\agent-benchmark') didn't match reality and
        # made every Run1 publish fail with a path-not-found error.
        'Run1' { Join-Path $BenchmarkRoot 'Run1\artifacts' }
        'Run2' { Join-Path $BenchmarkRoot 'Run2\artifacts' }
        'Run3' { Join-Path $BenchmarkRoot 'Run3\artifacts' }
        'Run4' { Join-Path $BenchmarkRoot 'Run4\artifacts' }
        'Run5' { Join-Path $BenchmarkRoot 'Run5\artifacts' }
        'Run5SameClient' { Join-Path $BenchmarkRoot 'Run5SameClient\artifacts' }
    }
} else { (Resolve-Path $ReportRoot).Path }
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "Benchmark report directory not found: $source"
}
# Run5SameClient publishes under the hyphenated "run5-same-client" slug to
# match the site's existing assets/manifest (assets/agent-benchmark-manifest.json)
# and the run-5b page, rather than the default lowercased run name.
$slug = if ($Run -eq 'Run5SameClient') { 'run5-same-client' } else { $Run.ToLowerInvariant() }
$destination = Join-Path $DestinationRoot $slug
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$files = Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object {
    $_.Name -in @('agent-benchmark.json','agent-benchmark.md','docker-metrics.csv','docker-metrics-summary.json','expected-state.json','run4-metadata.json','run4-summary.json','run5-metadata.json','run5-summary.json','run5-same-client-metadata.json','run5-same-client-summary.json')
}
if ($files.Count -eq 0) {
    throw "No publishable benchmark artifacts found under: $source"
}
foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length).TrimStart('\','/')
    $target = Join-Path $destination $relative
    $targetDir = Split-Path -Parent $target
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    Copy-Item -Force -LiteralPath $file.FullName -Destination $target
}
$manifestPath = Join-Path $destination 'publish-manifest.json'
$manifest = [ordered]@{
    run = $Run
    publishedUtc = (Get-Date).ToUniversalTime().ToString('o')
    source = $source
    destination = $destination
    files = @($files | ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($source.Length).TrimStart('\','/')
            bytes = $_.Length
            lastWriteUtc = $_.LastWriteTimeUtc.ToString('o')
        }
    })
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "Published $Run benchmark artifacts to:" -ForegroundColor Green
Write-Host "  $destination"
Write-Host "  Files: $($files.Count)"

# Regenerate the run-level aggregate from the exact raw source that was just
# published. This prevents the website matrix from drifting away from its raw
# benchmark evidence.
$aggregateScriptName = if ($Run -eq 'Run5SameClient') { 'build-run5b-aggregate.ps1' } else { "build-$($Run.ToLowerInvariant())-aggregate.ps1" }
$aggregateScript = Join-Path $BenchmarkRoot $aggregateScriptName
if (Test-Path -LiteralPath $aggregateScript -PathType Leaf) {
    $aggregateName = if ($Run -eq 'Run5SameClient') { 'run5b-aggregate.json' } else { "$($Run.ToLowerInvariant())-aggregate.json" }
    $aggregateDestination = Join-Path $DestinationRoot $aggregateName
    & $aggregateScript $source $aggregateDestination
    if (-not $?) { throw "Failed to regenerate aggregate for $Run." }
}
