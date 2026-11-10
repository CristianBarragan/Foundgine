#!/usr/bin/env bash
set -euo pipefail
RID="${1:-linux-x64}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

printf '== EF Core Native AOT publish (%s) ==\n' "$RID"
dotnet publish "$ROOT/Foundgine.Aot.EfCore/Foundgine.Aot.EfCore.csproj" \
  -c Release -r "$RID" --self-contained true -p:PublishAot=true \
  -p:SuppressTrimAnalysisWarnings=false -p:SuppressAotAnalysisWarnings=false
APP="$ROOT/Foundgine.Aot.EfCore/bin/Release/net10.0/$RID/publish/Foundgine.Aot.EfCore"
if [ ! -x "$APP" ]; then echo "Native executable missing: $APP" >&2; exit 1; fi
OUTPUT="$($APP 2>&1)"
printf '%s\n' "$OUTPUT"
grep -Fq 'Foundgine EF Core Native AOT pipeline passed (efcore): tenant isolation + generated map + projection.' <<< "$OUTPUT"
