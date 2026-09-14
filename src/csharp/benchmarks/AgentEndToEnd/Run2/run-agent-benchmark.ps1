[CmdletBinding()]
param(
    [ValidateSet('replay','live')]
    [string]$Mode = 'replay',
    # Accepted as a comma-separated string (e.g. "10,100,1000,10000"), not
    # [int[]], because this script is invoked as a brand-new `powershell.exe
    # -File ...` process by the orchestrator. Across that process boundary,
    # PowerShell does NOT greedily collect multiple space-separated tokens
    # into an array parameter the way an in-process call does - it only
    # binds the first value and then errors on the rest as unmatched
    # positional arguments. A single string token sidesteps that entirely.
    [string]$CustomerCounts = '10,100,1000,10000',
    [string]$Concurrency = '8,16,32,64',
    [int]$Warmups = 5,
    [int]$Runs = 30,
    [switch]$KeepDatabase,
    [switch]$SkipDockerBuild,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'

$customerCountsArray = $CustomerCounts -split ',' | ForEach-Object { [int]$_.Trim() }
$concurrencyArray = $Concurrency -split ',' | ForEach-Object { [int]$_.Trim() }
$runsPerTier = @($customerCountsArray | ForEach-Object { $Runs })

# run-agent-end-to-end.ps1 is invoked in-process via the call operator, so
# real array objects (not strings) are passed here - no splitting needed
# on the receiving end.
$child = Join-Path $PSScriptRoot 'run-agent-end-to-end.ps1'
& $child `
    -Mode $Mode `
    -CustomerCounts $customerCountsArray `
    -Concurrency $concurrencyArray `
    -RunsPerTier $runsPerTier `
    -Warmups $Warmups `
    -KeepDatabase:$KeepDatabase `
    -SkipDockerBuild:$SkipDockerBuild `
    -Publish:$Publish

if (-not $?) { exit 1 }
exit 0
