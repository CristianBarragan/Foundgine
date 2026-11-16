#!/usr/bin/env bash
# Production-readiness runner. Each stage writes evidence into $EV; `verdict` turns the evidence into the verdict.
#   bash readiness/run.sh --stage selftest --stage calibrate --stage verdict
#   stages: preflight selftest calibrate parity auth-attack entry e2e-up lib-aa lib e2e-perf faults soak tenant ops existing-suites scans verdict
#   quick (no Docker, no checkouts): selftest calibrate verdict
# env: FOUNDGINE_DIR E2E_DIR (checkouts)  COMPOSE_PROJECT_NAME (default foundgine-e2e)  EV WORK  SESSIONS FAULT_SESSIONS
#      SLO_P99_S TARGET_RATE SOAK_RATE SOAK_HOURS MAX_DRAIN_S RTO_S RPO_S LOADER_CPUS   flags: --overload
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd); export SUITE=$(dirname "$HERE")
export EV=${EV:-$SUITE/results/evidence}; export WORK=${WORK:-$SUITE/results/work}; export PY=${PY:-python3}
stages=()
while [ $# -gt 0 ]; do case $1 in
  --stage) stages+=("$2"); shift 2;;
  --overload) export OVERLOAD=1; shift;;
  -h|--help) sed -n 2,9p "$0"; exit 0;;
  *) echo "unknown arg $1" >&2; exit 2;; esac; done
[ ${#stages[@]} -gt 0 ] || { sed -n 2,9p "$0"; exit 2; }
rc=0
for s in "${stages[@]}"; do
  [ -f "$HERE/stages/$s.sh" ] || { echo "no such stage: $s" >&2; exit 2; }
  echo "### stage $s" >&2
  bash "$HERE/stages/$s.sh" || { echo "### stage $s exited $?" >&2; rc=1; }
done
exit $rc
