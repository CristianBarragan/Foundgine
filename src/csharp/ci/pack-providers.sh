#!/usr/bin/env bash
# Builds the Foundgine NuGet packages that a consumer needs for Foundgine.Providers and verifies the result.
# Usage: ci/pack-providers.sh <version> [output-dir]      e.g. ci/pack-providers.sh 3.3.3 ./artifacts
#
# Foundgine.Providers depends on Foundgine.Core and Foundgine.Runtime at the version it is packed with, so all
# packages are packed with the same explicit version. NuGet versions are immutable: never re-pack an already
# published version (e.g. 3.3.2), always bump.
set -euo pipefail
version="${1:?usage: $0 <version> [output-dir]}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$(mkdir -p "${2:-$root/artifacts}" && cd "${2:-$root/artifacts}" && pwd)"

for p in Foundgine.Core Foundgine.Runtime Foundgine.Providers Foundgine.Extensions; do
  dotnet pack "$root/$p/$p.csproj" -c Release -p:Version="$version" -o "$out"
done

"$root/ci/verify-providers-package.sh" "$out/Foundgine.Providers.$version.nupkg"
echo "Packages written to $out"
