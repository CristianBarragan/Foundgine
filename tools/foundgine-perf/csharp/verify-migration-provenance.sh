#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
BASE="$ROOT/src/csharp/benchmarks/SupplyChain.Performance/HotChocolate.SupplyChain.BenchmarkApi"
FG="$ROOT/src/csharp/benchmarks/SupplyChain.Performance/Foundgine.SupplyChain.BenchmarkApi"

fail() { echo "FAIL: $*" >&2; exit 1; }
[[ -d "$BASE/Migrations" ]] || fail "baseline EF migration directory is missing; generate it with dotnet-ef"
find "$BASE/Migrations" -maxdepth 1 -type f -name '*.cs' | grep -q . || fail "baseline EF migration source files are missing"
[[ -d "$FG/Migrations" ]] || fail "Foundgine EF migration directory is missing"
find "$FG/Migrations" -maxdepth 1 -type f -name '*.cs' | grep -q . || fail "Foundgine EF migration source files are missing"

if grep -R -nE 'EnsureCreated(Async)?\\s*\\(' "$BASE" "$FG" --include='*.cs'; then
  fail "EnsureCreated is prohibited for benchmark migration provenance"
fi

echo "SOURCE_PROVENANCE_OK"
echo "baseline: EF Core migration source files present"
echo "foundgine: independent Foundgine benchmark EF migration source files present"
echo "NOTE: this verifies source artifacts only; it does not prove generation history or successful application."
