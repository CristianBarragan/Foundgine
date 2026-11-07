[CmdletBinding()]
param(
    [ValidateSet('replay','live')]
    [string]$Mode = 'replay',
    [int]$Warmups = 1,
    [int]$Runs = 3,
    [switch]$NoInfrastructure,
    [switch]$KeepInfrastructure,
    [switch]$Publish
)

$script = Join-Path $PSScriptRoot 'benchmarks\AgentEndToEnd\run-agent-benchmark.ps1'
& $script @PSBoundParameters
exit $LASTEXITCODE
