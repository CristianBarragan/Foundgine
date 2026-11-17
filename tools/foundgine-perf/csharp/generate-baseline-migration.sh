#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
PROJECT="$ROOT/src/csharp/benchmarks/SupplyChain.Performance/HotChocolate.SupplyChain.BenchmarkApi"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK is required to generate the baseline EF Core migration" >&2
  exit 127
fi
if ! dotnet tool list --global | grep -q '^dotnet-ef '; then
  echo "Install dotnet-ef 10.0.x first: dotnet tool install --global dotnet-ef --version 10.0.14" >&2
  exit 2
fi
if compgen -G "$PROJECT/Migrations/*_Baseline.cs" >/dev/null; then
  echo "Baseline migration already exists; refusing to overwrite it" >&2
  exit 3
fi
dotnet ef migrations add Baseline --project "$PROJECT" --startup-project "$PROJECT"
echo "Generated ordinary EF Core migration in $PROJECT/Migrations"
