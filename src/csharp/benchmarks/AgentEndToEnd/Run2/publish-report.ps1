[CmdletBinding()]
param([string]$ReportRoot,[string]$Destination)
$common = Join-Path $PSScriptRoot '../publish-report-common.ps1'
if ([string]::IsNullOrWhiteSpace($ReportRoot)) { $ReportRoot = Join-Path $PSScriptRoot 'artifacts' }
& $common -Run Run2 -ReportRoot $ReportRoot -DestinationRoot $Destination
if (-not $?) { throw 'Run2 report publication failed.' }
