#!/usr/bin/env bash
# Fail fast before hours of runs: tools, checkouts, env vars per stage, free ports, disk. Writes no evidence.
#   bash readiness/run.sh --stage preflight          (set STAGES="entry faults ..." to check only what you plan to run)
source "$(dirname "$0")/../lib.sh"
STAGES=${STAGES:-"entry e2e-up lib-aa lib e2e-perf faults soak tenant ops existing-suites scans parity auth-attack"}
bad=0; ok() { printf '  ok    %s\n' "$*"; }; no() { printf '  MISSING %s\n' "$*"; bad=1; }
has() { case " $STAGES " in *" $1 "*) return 0;; esac; return 1; }
uses() { for s in "$@"; do has "$s" && return 0; done; return 1; }
t() { need "$1" && ok "$1" || no "$1 ($2)"; }
e() { [ -n "${!1:-}" ] && ok "\$$1" || no "\$$1 ($2)"; }
echo "stages checked: $STAGES"
t python3 "harness"; $PY -c 'import sys; sys.exit(sys.version_info < (3, 10))' && ok "python >= 3.10" || no "python >= 3.10"
uses entry e2e-up lib-aa lib e2e-perf faults soak tenant ops auth-attack parity && { t docker "all stack stages"; docker info >/dev/null 2>&1 && ok "docker daemon reachable" || no "docker daemon not reachable"; }
uses entry e2e-up existing-suites scans && { t git "commit pin for prepare-e2e"; e FOUNDGINE_DIR "checkout under test"; [ -d "${FOUNDGINE_DIR:-/nonexistent}" ] && ok "FOUNDGINE_DIR exists" || no "FOUNDGINE_DIR is not a directory"; }
uses entry e2e-up existing-suites && { t dotnet ".NET SDK 10 (global.json)"; t mvn "Maven"; t java "JDK; e2e ERP needs 27, orchestration 21 (built in Docker)"; }
uses e2e-up && { e E2E_DIR "Foundgine-e2e-example checkout"; [ -d "${E2E_DIR:-/nonexistent}" ] && ok "E2E_DIR exists" || no "E2E_DIR is not a directory"; }
uses faults soak ops && e CRM_DB_PASSWORD "SA password of the CRM SQL Server container (invariants + backup drill)"
uses soak && e SOAK_RATE "60% of the sustainable rate"
uses auth-attack && e AUTH_ENDPOINTS "protected GET URLs"
uses e2e-perf && { e SLO_P99_S "agreed p99 SLO in seconds"; e TARGET_RATE "target journeys/s"; }
uses scans && { for x in osv-scanner gitleaks syft; do need "$x" && ok "$x" || echo "  warn  $x missing: R16 will be not_run"; done; }
uses entry lib-aa lib && for p in ${POSTGRES_HOST_PORT:-55432} ${REDIS_HOST_PORT:-56379} 4300 4302; do
  (exec 3<>"/dev/tcp/127.0.0.1/$p") 2>/dev/null && echo "  warn  port $p already in use (fine if it is the stack you want reused)" || ok "port $p free"; done
free=$(df -Pk "$WORK" | awk 'NR==2{print int($4/1048576)}'); [ "${free:-0}" -ge 20 ] && ok "disk ${free} GB free" || no "disk: ${free:-?} GB free, want >= 20 (images, 24 h soak logs)"
if need shellcheck; then shellcheck -S warning "$SUITE"/readiness/*.sh "$SUITE"/readiness/stages/*.sh && ok shellcheck || echo "  warn  shellcheck findings above"; else echo "  warn  shellcheck not installed (recommended once, on the real host)"; fi
[ $bad = 0 ] && echo "PREFLIGHT OK" || { echo "PREFLIGHT FAILED: fix the MISSING items first"; exit 1; }
