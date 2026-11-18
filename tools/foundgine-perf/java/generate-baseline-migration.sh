#!/usr/bin/env bash
# The Java baseline migration is hand-authored Flyway SQL (V1__baseline.sql), not generated. This
# script exists for symmetry with csharp/ and only reports whether the file is in place.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
FILE="$ROOT/src/java/benchmarks/supply-chain/spring-baseline/src/main/resources/db/migration/V1__baseline.sql"
if [[ -f "$FILE" ]]; then
  echo "Baseline migration present (hand-authored, independent of foundgine-migrations): $FILE"
else
  echo "Baseline migration is missing: $FILE" >&2
  exit 1
fi
