#!/usr/bin/env bash
# EG1-EG4. Redis and PostgreSQL come from the Foundgine compose files, so adapter tests run against real services (EG3).
source "$(dirname "$0")/../lib.sh"; need_dirs; need dotnet || die "dotnet SDK not found"; need mvn || die "mvn not found"; need docker || die "docker not found"
PG=${POSTGRES_HOST_PORT:-55432}; RD=${REDIS_HOST_PORT:-56379}; PG16=${POSTGRES16_HOST_PORT:-55433}
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
# Three different failures used to collapse into one "EG1: fail". Keep them apart so the cause is printed, not just the verdict.
# Opt-in suites that skip by design unless their own fixture is running (own compose file / database extension image).
# They are excluded here and the exclusion is written into the EG1 evidence, so "zero skipped" still means something.
# EG1_FILTER="" runs everything (they will then count as skipped); EG1_FILTER='<vstest filter>' overrides.
OPTIN_EXCLUDE='FullyQualifiedName!~Foundgine.SupplyChain.PenTest.Tests&FullyQualifiedName!~SupplyChainGraphSimilarityRetrievalTests&FullyQualifiedName!~SupplyChainSearchRetrievalTests'
EG1_FILTER=${EG1_FILTER-$OPTIN_EXCLUDE}
filter_args=(); [ -n "$EG1_FILTER" ] && filter_args=(--filter "$EG1_FILTER")
ok1=1; why1=""
if ! dotnet build "$SLN" -c Release >"$RES/dotnet-build.log" 2>&1; then ok1=0; why1="build"
# results-directory belongs BEFORE the `--`; after it VSTest only accepts name=value RunSettings pairs, so a bare flag there is rejected.
elif ! dotnet test "$SLN" -c Release --no-build ${filter_args[@]+"${filter_args[@]}"} --logger "trx" --results-directory "$RES/trx" -- xUnit.ParallelizeTestCollections=false >"$RES/dotnet-test.log" 2>&1; then ok1=0; why1="test"; fi
T=$($PY "$SUITE/readiness/parse_results.py" trx "$RES/trx" 'redis|migrat|agentframework|maf|postgres' 2>/dev/null) || { [ "$ok1" = 1 ] && why1="results (no tests ran, or some skipped)"; ok1=0; }
[ -n "$T" ] && echo "$T" > "$RES/trx.json" || echo "{\"totals\":{}}" > "$RES/trx.json"
if [ "$ok1" = 0 ]; then
  say "EG1 diagnosis: failed at stage '$why1'"
  case "$why1" in
    build) { grep -E "error [A-Za-z]+[0-9]+" "$RES/dotnet-build.log" | sed 's/ \[.*\]$//' | sort -u | head -15 >&2; } || true
           echo "(full log: $RES/dotnet-build.log; NU190x = NuGet advisory promoted by TreatWarningsAsErrors; NU1301 = restore/offline)" >&2;;
    test)  { grep -E "error:|runsettings" "$RES/dotnet-test.log" | head -5 >&2; } || true   # diagnostics must never abort the stage (set -e + pipefail + grep no-match)
           $PY "$SUITE/readiness/parse_results.py" diag "$RES/trx" >&2 || true; echo "(full log: $RES/dotnet-test.log)" >&2;;
    *)     $PY "$SUITE/readiness/parse_results.py" diag "$RES/trx" >&2 || true; $PY -c 'import json,sys;print("totals:",json.load(open(sys.argv[1]))["totals"])' "$RES/trx.json" >&2;;
  esac
fi
[ "$ok1" = 1 ] && rec EG1 pass --measured "$($PY -c 'import json,sys;print(json.dumps(json.load(open(sys.argv[1]))["totals"]))' "$RES/trx.json")" --threshold "build ok, >0 passed, 0 failed, 0 skipped" --detail "test filter: ${EG1_FILTER:-none (all tests)}" --artifact "$RES/trx.json" \
 || rec EG1 fail --detail "failed at $why1: $(grep -hE 'error [A-Za-z]+[0-9]+|^\s*Failed ' "$RES/dotnet-build.log" "$RES/dotnet-test.log" 2>/dev/null | head -3 | tr '\n' ' ' | cut -c1-250)" --artifact "$RES/dotnet-test.log"
# EG1_ONLY=1 stops here so EG1 can be iterated on without paying for the Maven build.
[ "${EG1_ONLY:-}" = 1 ] && exit 0

# AdvancedPostgres16SchemaIntegrationTest skips unless FOUNDGINE_POSTGRES16_JDBC_URL is set, and asserts the server is exactly PostgreSQL 16.
# The compose file runs pg17, so a disposable postgres:16 container is started just for the Maven run and removed afterwards.
say "start disposable PostgreSQL 16 (AdvancedPostgres16SchemaIntegrationTest asserts server 16.x; the compose file is pg17)"
docker rm -f fg-pg16-entry >/dev/null 2>&1 || true
docker run -d --name fg-pg16-entry -e POSTGRES_USER=foundgine -e POSTGRES_PASSWORD=foundgine \
  -e POSTGRES_DB=foundgine_pg16 -p "$PG16:5432" postgres:16 >/dev/null \
  || die "could not start postgres:16 on port $PG16 (busy? set POSTGRES16_HOST_PORT)"
trap 'docker rm -f fg-pg16-entry >/dev/null 2>&1 || true' EXIT
# -h 127.0.0.1 forces TCP: the image's init-time temp server listens only on the unix socket, so a socket check would pass too early.
ready16=0
for _ in $(seq 1 60); do docker exec fg-pg16-entry pg_isready -h 127.0.0.1 -U foundgine -d foundgine_pg16 >/dev/null 2>&1 && { ready16=1; break; }; sleep 1; done
[ "$ready16" = 1 ] || die "postgres:16 did not become ready on port $PG16 (docker logs fg-pg16-entry)"

# clean: an interrupted earlier run leaves a half-written target/ (compile errors) and stale surefire reports that parse_results.py would count as evidence.
say "EG2: mvn clean verify (gpg signing skipped: the release signing key is not on this machine)"
ok2=1; env FOUNDGINE_POSTGRES_CONNECTION_STRING="$JDBC_CRED" FOUNDGINE_TEST_PG_URL="$JDBC_CRED" FOUNDGINE_JPA_POSTGRES_URL="$JDBC_PLAIN" FOUNDGINE_JPA_POSTGRES_USER=foundgine FOUNDGINE_JPA_POSTGRES_PASSWORD=foundgine \
  FOUNDGINE_POSTGRES16_JDBC_URL="jdbc:postgresql://localhost:$PG16/foundgine_pg16" FOUNDGINE_POSTGRES16_USER=foundgine FOUNDGINE_POSTGRES16_PASSWORD=foundgine \
  mvn -B -Dgpg.skip=true -f "$FG/src/java/pom.xml" clean verify >"$RES/mvn.log" 2>&1 || ok2=0
docker rm -f fg-pg16-entry >/dev/null 2>&1 || true
S=$($PY "$SUITE/readiness/parse_results.py" surefire "$FG/src/java" 'redis|migrat|postgres' 2>/dev/null) || ok2=0
[ -n "$S" ] && echo "$S" > "$RES/surefire.json" || echo "{\"totals\":{}}" > "$RES/surefire.json"
if [ "$ok2" = 0 ]; then   # same rule as EG1: say why, not just that. Every diagnostic line is guarded so it can never abort the stage.
  say "EG2 diagnosis"
  { sed 's/\x1b\[[0-9;]*m//g' "$RES/mvn.log" | grep -E "^\[ERROR\]|Tests run:.*(Failures: [1-9]|Errors: [1-9])|BUILD (FAILURE|SUCCESS)" | awk '!seen[$0]++' | head -25 >&2; } || true
  $PY -c 'import json,sys;print("surefire totals:",json.load(open(sys.argv[1]))["totals"])' "$RES/surefire.json" >&2 || true
  echo "(full log: $RES/mvn.log)" >&2
fi
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