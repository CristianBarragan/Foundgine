#!/usr/bin/env bash
# Verifies that a packed Foundgine.Providers .nupkg really carries the EF Core provider, the shared ORM core and both
# build-time generators, and that a consumer needs nothing but this one Foundgine package.
# Usage: ci/verify-providers-package.sh path/to/Foundgine.Providers.<version>.nupkg
#
# The EF Core provider and ORM core are shipped as their own assemblies next to Foundgine.Providers.dll
# (lib/<tfm>/Foundgine.Providers.EntityFrameworkCore.dll and lib/<tfm>/Foundgine.Providers.Orm.dll), so the type
# checks below look in those assemblies, not in Foundgine.Providers.dll.
set -euo pipefail
pkg="${1:?usage: $0 <Foundgine.Providers.nupkg>}"
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
unzip -q "$pkg" -d "$work"
fail=0

# Type names are stored as UTF-8 strings in the assembly metadata, so grep -a finds them without a .NET runtime.
require_assembly_types() {
  local name="$1"; shift
  local dll; dll="$(find "$work/lib" -name "$name.dll" | head -n1 || true)"
  if [[ -z "$dll" ]]; then echo "MISSING: lib/*/$name.dll"; fail=1; return; fi
  for t in "$@"; do
    grep -aq "$t" "$dll" || { echo "MISSING type in $name.dll: $t"; fail=1; }
  done
}

require_assembly_types Foundgine.Providers
require_assembly_types Foundgine.Providers.Orm OrmPlanCompiler OrmEntityMap OrmExecutionProvider
require_assembly_types Foundgine.Providers.EntityFrameworkCore \
  FoundgineEfRepository FoundgineEfReader FoundgineEfModel EfCoreExecutionProvider AddFoundgineEf

for a in Foundgine.Providers.Aot.Generator.dll Foundgine.Providers.Orm.Generator.dll; do
  [[ -f "$work/analyzers/dotnet/cs/$a" ]] || { echo "MISSING analyzer: analyzers/dotnet/cs/$a"; fail=1; }
done

# EF Core is restored from NuGet, never bundled.
for d in Microsoft.EntityFrameworkCore Microsoft.EntityFrameworkCore.Relational Microsoft.Extensions.DependencyInjection.Abstractions; do
  grep -q "id=\"$d\"" "$work"/*.nuspec || { echo "MISSING nuspec dependency: $d"; fail=1; }
  if find "$work/lib" -name "$d.dll" | grep -q .; then echo "UNEXPECTED bundled third-party DLL: $d.dll"; fail=1; fi
done

# The Foundgine-owned ORM/EF assemblies must not leak out as separate package dependencies, otherwise a consumer
# would need Foundgine.Providers.Orm / .EntityFrameworkCore packages that are never published.
for d in Foundgine.Providers.Orm Foundgine.Providers.EntityFrameworkCore Foundgine.Providers.Aot.Generator Foundgine.Providers.Orm.Generator; do
  if grep -q "id=\"$d\"" "$work"/*.nuspec; then echo "UNEXPECTED nuspec dependency on $d (must be bundled, not a dependency)"; fail=1; fi
done

if [[ $fail -eq 0 ]]; then echo "OK: $pkg contains the EF Core provider, ORM core and both generators."; fi
exit $fail
