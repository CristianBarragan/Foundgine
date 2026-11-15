#!/usr/bin/env bash
# Fails when a copy of a top-level project is nested inside another directory, e.g. Foundgine.Providers/Foundgine.Providers.Orm
# next to the real Foundgine.Providers.Orm. This happens when a zip is extracted over an older tree: the old copy survives,
# diverges from the real project and gets edited by mistake. Legitimately nested projects (samples, benchmarks, the Aot
# generator) have no top-level twin and are not flagged.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
status=0
while IFS= read -r csproj; do
  dir="$(dirname "$csproj")"
  name="$(basename "$dir")"
  [[ "$dir" == "$root/$name" ]] && continue               # this is the top-level project itself
  if [[ -f "$root/$name/$name.csproj" ]]; then
    echo "stale nested copy of top-level project '$name': ${dir#"$root"/}" >&2
    status=1
  fi
done < <(find "$root" -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/node_modules/*')
exit $status
