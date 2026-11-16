#!/usr/bin/env bash
# EG1-EG4. Redis and PostgreSQL come from the Foundgine compose files, so adapter tests run against real services (EG3).
source "$(dirname "$0")/../lib.sh"; need_dirs; need dotnet || die "dotnet SDK not found"; need mvn || die "mvn not found"; need docker || die "docker not found"
PG=${POSTGRES_HOST_PORT:-55432}; RD=${REDIS_HOST_PORT:-56379}
say "start Redis and PostgreSQL"
(cd "$FG" && docker compose -f docker-compose.redis.yml up -d --wait && docker compose -f docker-compose.postgres.yml up -d --wait)
# Names taken from the tests' own getenv calls (counted in the repo); values follow docker-compose.postgres.yml.
# ADO.NET connection string for the C# integration tests.
export FOUNDGINE_POSTGRES_CONNECTION_STRING="Host=localhost;Port=$PG;Database=foundgine_e2e;Username=foundgine;Password=foundgine"
export FOUNDGINE_POSTGRES_CONNECTION="$FOUNDGINE_POSTGRES_CONNECTION_STRING"
export FOUNDGINE_REDIS="localhost:$RD"
# Java tests: the PG integration tests take a JDBC URL that carries its own credentials; the JPA tests read the
# user/password separately and otherwise default to postgres/postgres, which the compose database rejects.
export FOUNDGINE_TEST_PG_URL="jdbc:postgresql://localhost:$PG/foundgine_e2e?user=foundgine&password=foundgine"
export FOUNDGINE_JPA_POSTGRES_URL="jdbc:postgresql://localhost:$PG/foundgine_e2e"
export FOUNDGINE_JPA_POSTGRES_USER=foundgine FOUNDGINE_JPA_POSTGRES_PASSWORD=foundgine
# Migration tooling (MigrationsCli).
export FOUNDGINE_JDBC_URL="jdbc:postgresql://localhost:$PG/foundgine_e2e" FOUNDGINE_JDBC_USER=foundgine FOUNDGINE_JDBC_PASSWORD=foundgine

# AdvancedPostgres16SchemaIntegrationTest is opt-in and asserts server_version 16.x (compose runs pg17), so it would be
# skipped and fail the "0 skipped" gate. Give it a disposable PostgreSQL 16 on its own port; removed on exit.
PG16=${POSTGRES16_HOST_PORT:-55433}; PG16_NAME=foundgine-perf-pg16
docker rm -f "$PG16_NAME" >/dev/null 2>&1 || true
docker run -d --name "$PG16_NAME" -e POSTGRES_USER=foundgine -e POSTGRES_PASSWORD=foundgine -e POSTGRES_DB=foundgine_pg16 -p "$PG16":5432 postgres:16 >/dev/null
trap 'docker rm -f "$PG16_NAME" >/dev/null 2>&1 || true' EXIT
for _ in $(seq 1 60); do docker exec "$PG16_NAME" pg_isready -U foundgine -d foundgine_pg16 >/dev/null 2>&1 && break; sleep 1; done
docker exec "$PG16_NAME" pg_isready -U foundgine -d foundgine_pg16 >/dev/null 2>&1 || die "postgres:16 did not become ready"
export FOUNDGINE_POSTGRES16_JDBC_URL="jdbc:postgresql://localhost:$PG16/foundgine_pg16" FOUNDGINE_POSTGRES16_USER=foundgine FOUNDGINE_POSTGRES16_PASSWORD=foundgine

RES=$WORK/entry; rm -rf "$RES"; mkdir -p "$RES"
SLN=${FG_SLN:-$(find "$FG/src/csharp" -maxdepth 2 \( -name "*.sln" -o -name "*.slnx" \) | head -1)}; [ -n "$SLN" ] || die "no .sln under $FG/src/csharp (set FG_SLN)"
say "EG1: dotnet build + test ($SLN)"
ok1=1; dotnet build "$SLN" -c Release >"$RES/dotnet-build.log" 2>&1 && dotnet test "$SLN" -c Release --no-build --logger "trx" --results-directory "$RES/trx" >"$RES/dotnet-test.log" 2>&1 || ok1=0
T=$($PY "$SUITE/readiness/parse_results.py" trx "$RES/trx" 'redis|migrat|agentframework|maf|postgres' 2>/dev/null) || ok1=0
[ -n "$T" ] && echo "$T" > "$RES/trx.json" || echo "{\"totals\":{}}" > "$RES/trx.json"
[ "$ok1" = 1 ] && rec EG1 pass --measured "$($PY -c 'import json,sys;print(json.dumps(json.load(open(sys.argv[1]))["totals"]))' "$RES/trx.json")" --threshold "build ok, >0 passed, 0 failed, 0 skipped" --artifact "$RES/trx.json" \
 || rec EG1 fail --detail "build/test failed or tests were skipped: $(tail -n 3 "$RES/dotnet-test.log" | tr '\n' ' ' | cut -c1-250)" --artifact "$RES/dotnet-test.log"
say "EG2: mvn verify"
ok2=1; mvn -B -f "$FG/src/java/pom.xml" -Dgpg.skip=true verify >"$RES/mvn.log" 2>&1 || ok2=0
S=$($PY "$SUITE/readiness/parse_results.py" surefire "$FG/src/java" 'redis|migrat|postgres' 2>/dev/null) || ok2=0
[ -n "$S" ] && echo "$S" > "$RES/surefire.json" || echo "{\"totals\":{}}" > "$RES/surefire.json"
[ "$ok2" = 1 ] && rec EG2 pass --measured "$($PY -c 'import json,sys;print(json.dumps(json.load(open(sys.argv[1]))["totals"]))' "$RES/surefire.json")" --artifact "$RES/surefire.json" \
 || rec EG2 fail --detail "mvn verify failed or tests skipped: $(grep -E 'ERROR|FAIL' "$RES/mvn.log" | head -3 | tr '\n' ' ' | cut -c1-250)" --artifact "$RES/mvn.log"
say "EG3: adapter tests actually ran"
miss=$($PY - "$RES" <<'P'
import json, sys
d = sys.argv[1]; need = {"redis": 0, "postgres": 0}
try:
    names = [n.lower() for n in json.load(open(f"{d}/trx.json"))["totals"].get("matching", [])] + [n.lower() for n in json.load(open(f"{d}/surefire.json"))["totals"].get("matching", [])]
except Exception: names = []
print(",".join(k for k in need if not any(k in n for n in names)))
P
)
if [ "$ok1" = 1 ] && [ "$ok2" = 1 ] && [ -z "$miss" ]; then rec EG3 pass --detail "Redis+PostgreSQL up during both suites; matching test units ran with 0 skipped"
else rec EG3 fail --detail "ok1=$ok1 ok2=$ok2 no ran tests matching: ${miss:-none}"; fi
say "EG4: e2e example"; bash "$SUITE/readiness/stages/e2e-up.sh" || true
say "EG6 is manual: compare the CI PostgreSQL image tag with the schema-parity test, then record it (see criteria.json)"