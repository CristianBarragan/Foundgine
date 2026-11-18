#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
BASE="$ROOT/src/java/benchmarks/supply-chain/spring-baseline"
FG="$ROOT/src/java/benchmarks/supply-chain/foundgine"
SAMPLE="$ROOT/src/java/samples/foundgine-supply-chain"

fail() { echo "FAIL: $*" >&2; exit 1; }
[[ -f "$BASE/src/main/resources/db/migration/V1__baseline.sql" ]] || fail "baseline Flyway migration V1__baseline.sql is missing"
compgen -G "$SAMPLE/src/main/resources/db/migration/V*__InitialCreate.sql" >/dev/null \
  || fail "Foundgine generated migration (V*__InitialCreate.sql) is missing; run java/generate-foundgine-migration.sh"
if compgen -G "$FG/src/main/resources/db/migration/*.sql" >/dev/null; then
  fail "the Foundgine arm must not carry its own copy of the migration; it resolves it from the sample dependency"
fi
grep -q 'ddl-auto: validate' "$BASE/src/main/resources/application.yml" || fail "baseline must set Hibernate ddl-auto: validate"
if grep -RnE 'ddl-auto: *(create|update|create-drop)|hbm2ddl' "$BASE/src" "$FG/src"; then
  fail "Hibernate schema generation is prohibited for benchmark migration provenance"
fi

echo "SOURCE_PROVENANCE_OK"
echo "baseline: hand-authored Flyway V1__baseline.sql present, Hibernate validates only"
echo "foundgine: foundgine-migrations output present in the sample dependency, no local copy"
echo "NOTE: this verifies source artifacts only; it does not prove generation history or successful application."
