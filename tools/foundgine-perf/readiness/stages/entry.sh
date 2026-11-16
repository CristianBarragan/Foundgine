#!/usr/bin/env bash
# EG1-EG4. Redis and PostgreSQL come from the Foundgine compose files, so adapter tests run against real services (EG3).
source "$(dirname "$0")/../lib.sh"; need_dirs; need dotnet || die "dotnet SDK not found"; need mvn || die "mvn not found"; need docker || die "docker not found"
PG=${POSTGRES_HOST_PORT:-55432}; RD=${REDIS_HOST_PORT:-56379}
export COMPOSE_IGNORE_ORPHANS=1   # the two compose files share a project name; --remove-orphans would stop the other service
say "start Redis and PostgreSQL (fresh PostgreSQL volume: postgres-init.sql only runs on an empty data volume, and the E2E tests expect its fg_* schemas and a clean state)"
(cd "$FG" && docker compose -f docker-compose.postgres.yml down -v >/dev/null 2>&1) || true
(cd "$FG" && docker compose -f docker-compose.redis.yml up -d --wait && docker compose -f docker-compose.postgres.yml up -d --wait) || die "docker compose up --wait failed for Redis/PostgreSQL (ports $RD/$PG busy? run: docker ps; docker compose -f docker-compose.redis.yml logs)"
# Names taken from the tests' own getenv calls (counted in the repo); values follow docker-compose.postgres.yml.
export FOUNDGINE_POSTGRES_CONNECTION_STRING="Host=localhost;Port=$PG;Database=foundgine_e2e;Username=foundgine;Password=foundgine"
export FOUNDGINE_POSTGRES_CONNECTION="$FOUNDGINE_POSTGRES_CONNECTION_STRING" FOUNDGINE_REDIS="localhost:$RD"
# .NET tests read the Npgsql-style FOUNDGINE_POSTGRES_CONNECTION_STRING exported above. The Java tests read the SAME name as a JDBC URL
# (some pass it straight to DriverManager, so it must carry user and password), so the Java values are scoped to the mvn call only.
# Assign first, then reference: inside one `export A=.. B="$A"` the $A is expanded before A exists (unbound variable under set -u).
JDBC_PLAIN="jdbc:postgresql://localhost:$PG/foundgine_e2e"
JDBC_CRED="$JDBC_PLAIN?user=foundgine&password=foundgine"
RES=$WORK/entry; rm -rf "$RES"; mkdir -p "$RES"
# Foundgine.sln is the full solution; Foundgine.Orm.slnx is a subset and `find | head -1` picked whichever came first. Prefer the .sln, then sort.
SLN=${FG_SLN:-$(find "$FG/src/csharp" -maxdepth 2 -name "*.sln" | sort | sed -n 1p)}
[ -n "$SLN" ] || SLN=$(find "$FG/src/csharp" -maxdepth 2 -name "*.slnx" | sort | sed -n 1p)
[ -n "$SLN" ] || die "no .sln under $FG/src/csharp (set FG_SLN)"
say "EG1: dotnet build + test ($SLN)"; echo "using solution: $SLN" >&2
ok1=1; dotnet build "$SLN" -c Release >"$RES/dotnet-build.log" 2>&1 && dotnet test "$SLN" -c Release --no-build --logger "trx" --results-directory "$RES/trx" >"$RES/dotnet-test.log" 2>&1 || ok1=0
T=$($PY "$SUITE/readiness/parse_results.py" trx "$RES/trx" 'redis|migrat|agentframework|maf|postgres' 2>/dev/null) || ok1=0
[ -n "$T" ] && echo "$T" > "$RES/trx.json" || echo "{\"totals\":{}}" > "$RES/trx.json"
[ "$ok1" = 1 ] && rec EG1 pass --measured "$($PY -c 'import json,sys;print(json.dumps(json.load(open(sys.argv[1]))["totals"]))' "$RES/trx.json")" --threshold "build ok, >0 passed, 0 failed, 0 skipped" --artifact "$RES/trx.json" \
 || rec EG1 fail --detail "build/test failed or tests were skipped: $(tail -n 3 "$RES/dotnet-test.log" | tr '\n' ' ' | cut -c1-250)" --artifact "$RES/dotnet-test.log"
# clean: an interrupted earlier run leaves a half-written target/ (compile errors) and stale surefire reports that parse_results.py would count as evidence.
say "EG2: mvn clean verify (gpg signing skipped: the release signing key is not on this machine)"
ok2=1; env FOUNDGINE_POSTGRES_CONNECTION_STRING="$JDBC_CRED" FOUNDGINE_TEST_PG_URL="$JDBC_CRED" FOUNDGINE_JPA_POSTGRES_URL="$JDBC_PLAIN" FOUNDGINE_JPA_POSTGRES_USER=foundgine FOUNDGINE_JPA_POSTGRES_PASSWORD=foundgine \
  mvn -B -Dgpg.skip=true -f "$FG/src/java/pom.xml" clean verify >"$RES/mvn.log" 2>&1 || ok2=0
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