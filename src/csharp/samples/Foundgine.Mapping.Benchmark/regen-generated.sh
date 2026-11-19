#!/usr/bin/env bash
# Regenerates Generated/ScenarioMappings.g.cs from the fluent-built scenario models, then rebuilds and runs the checks.
set -euo pipefail
cd "$(dirname "$0")"
dotnet build -c Release -v q
dotnet run -c Release --no-build -- emit Generated/ScenarioMappings.g.cs
dotnet build -c Release -v q
dotnet run -c Release --no-build
