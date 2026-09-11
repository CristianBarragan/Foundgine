$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "pipelines\query.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
