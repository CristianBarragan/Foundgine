[CmdletBinding()]
param([string]$ReportRoot,[string]$Destination)
$common = Join-Path $PSScriptRoot '../publish-report-common.ps1'
if ([string]::IsNullOrWhiteSpace($ReportRoot)) { $ReportRoot = Join-Path $PSScriptRoot 'artifacts' }
& $common -Run Run3 -ReportRoot $ReportRoot -DestinationRoot $Destination
if (-not $?) { throw 'Run3 report publication failed.' }
