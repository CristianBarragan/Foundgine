#!/usr/bin/env bash
# Verifies that a packed Foundgine.Providers .nupkg really carries the EF Core provider, the shared ORM core and both
# build-time generators. Usage: ci/verify-providers-package.sh path/to/Foundgine.Providers.<version>.nupkg
set -euo pipefail
pkg="${1:?usage: $0 <Foundgine.Providers.nupkg>}"
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
unzip -q "$pkg" -d "$work"
fail=0

dll="$(find "$work/lib" -name 'Foundgine.Providers.dll' | head -n1 || true)"
if [[ -z "$dll" ]]; then echo "MISSING: lib/*/Foundgine.Providers.dll"; fail=1; fi

# Type names are stored as UTF-8 strings in the assembly metadata, so grep -a finds them without a .NET runtime.
if [[ -n "$dll" ]]; then
  for t in FoundgineEfRepository FoundgineEfReader FoundgineEfModel EfCoreExecutionProvider OrmPlanCompiler AddFoundgineEf; do
    if ! grep -aq "$t" "$dll"; then echo "MISSING type in Foundgine.Providers.dll: $t"; fail=1; fi
  done
fi

for a in Foundgine.Providers.Aot.Generator.dll Foundgine.Providers.Orm.Generator.dll; do
  [[ -f "$work/analyzers/dotnet/cs/$a" ]] || { echo "MISSING analyzer: analyzers/dotnet/cs/$a"; fail=1; }
done

for d in Microsoft.EntityFrameworkCore Microsoft.EntityFrameworkCore.Relational; do
  grep -q "id=\"$d\"" "$work"/*.nuspec || { echo "MISSING nuspec dependency: $d"; fail=1; }
done

if [[ $fail -eq 0 ]]; then echo "OK: $pkg contains the EF Core provider, ORM core and both generators."; fi
exit $fail
