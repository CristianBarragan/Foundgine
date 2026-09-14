[CmdletBinding()]param([string]$CustomerCounts='10,100,1000,10000',[string]$Concurrency='8,16,32,64',[int]$Warmups=5,[int]$Runs=30,[switch]$Publish)
$runsPerTier = (($CustomerCounts -split ',') | ForEach-Object { $Runs }) -join ','
& (Join-Path $PSScriptRoot 'run-run5.ps1') `
    -CustomerCounts $CustomerCounts `
    -Concurrency $Concurrency `
    -RunsPerTier $runsPerTier `
    -Warmups $Warmups `
    -Publish:$Publish
if(!$?){exit 1};exit 0
