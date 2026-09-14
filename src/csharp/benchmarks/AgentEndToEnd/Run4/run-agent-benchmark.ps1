[CmdletBinding()]
param(
    [ValidateSet('agent','protocol','both')]
    [string]$Mode = 'both',
    # See Run2\run-agent-benchmark.ps1 for why this is a comma-separated
    # string rather than [int[]] - it must survive a `powershell.exe -File`
    # process boundary, where array parameters don't greedily collect
    # multiple space-separated tokens.
    [string]$CustomerCounts = '10,100,1000,10000',
    [string]$Concurrency = '8,16,32,64',
    [int]$Warmups = 5,
    [int]$Runs = 30,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'

$customerCountsArray = $CustomerCounts -split ',' | ForEach-Object { [int]$_.Trim() }
$concurrencyArray = $Concurrency -split ',' | ForEach-Object { [int]$_.Trim() }
$runsPerTier = @($customerCountsArray | ForEach-Object { $Runs })

$child = Join-Path $PSScriptRoot 'run-run4.ps1'
& $child `
    -Mode $Mode `
    -CustomerCounts $customerCountsArray `
    -Concurrency $concurrencyArray `
    -RunsPerTier $runsPerTier `
    -Warmups $Warmups `
    -Publish:$Publish

if (-not $?) { exit 1 }
exit 0
