#!/usr/bin/env bash
# Builds the Foundgine NuGet packages (core, runtime, providers, and each optional provider/extension integration) and verifies the result.
# Usage: ci/pack-providers.sh <version> [output-dir]      e.g. ci/pack-providers.sh 3.4.0 ./artifacts
#
# Every package depends on Foundgine.Core/Runtime/Providers at the version it is packed with, so all
# packages are packed with the same explicit version. NuGet versions are immutable: never re-pack an already
# published version (e.g. 3.4.0), always bump.
set -euo pipefail
version="${1:?usage: $0 <version> [output-dir]}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$(mkdir -p "${2:-$root/artifacts}" && cd "${2:-$root/artifacts}" && pwd)"

bash "$root/ci/verify-no-stale-nested-projects.sh"

for p in Foundgine.Core Foundgine.Runtime Foundgine.Providers \
         Foundgine.Providers.Redis Foundgine.Providers.AgentFramework Foundgine.Providers.PostgresVector Foundgine.Providers.Elasticsearch \
         Foundgine.Extensions.GraphQL Foundgine.Extensions.Temporal Foundgine.Extensions.Keycloak; do
  dotnet pack "$root/$p/$p.csproj" -c Release -p:Version="$version" -o "$out"
done

bash "$root/ci/verify-providers-package.sh" "$out/Foundgine.Providers.$version.nupkg"
echo "Packages written to $out"
