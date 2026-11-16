#!/usr/bin/env bash
# Wrapper for tools/foundgine-perf/readiness/run.sh
# Put this file in tools/foundgine-perf/ and run:   bash foundgine-perf.sh <phase>
#   preflight  check the host only (no stack needed)
#   perf       e2e-up, e2e-perf, lib-aa, lib          (the performance numbers)
#   full       perf + entry, tenant, ops, faults (~3h), auth-attack, existing-suites, scans, verdict
#   soak       24h soak; needs SOAK_RATE (0.6 x sustainable rate found by e2e-perf)
#   verdict    summarise evidence collected so far
set -euo pipefail

# ---- edit these -------------------------------------------------------------
export FOUNDGINE_DIR="${FOUNDGINE_DIR:-$HOME/work/Foundgine}"
export E2E_DIR="${E2E_DIR:-$HOME/work/Foundgine-e2e-example}"
# Only needed if FOUNDGINE_DIR is not a git checkout (e.g. unzipped): export FOUNDGINE_COMMIT=<40-char sha>

# Default sa password from the e2e example's .env.example
export CRM_DB_PASSWORD="${CRM_DB_PASSWORD:-Crm_dev_pw1!}"

# Targets the e2e results are judged against (replace with your real numbers)
export SLO_P99_S="${SLO_P99_S:-30}"          # p99 journey latency target, seconds
export TARGET_RATE="${TARGET_RATE:-10}"      # customer-to-cash journeys per second you must sustain

# Protected GET URLs for the auth-attack stage (space separated)
export AUTH_ENDPOINTS="${AUTH_ENDPOINTS:-http://127.0.0.1:5000/api/v1/customers http://127.0.0.1:8082/api/v1/orders/1}"

# Optional: pin the load generator away from the containers, e.g. "6,7"
# export LOADER_CPUS=6,7
# export SESSIONS=5 FAULT_SESSIONS=2
# -----------------------------------------------------------------------------

cd "$(dirname "$0")"
RUN="bash readiness/run.sh"
phase="${1:-}"

case "$phase" in
  preflight)
    STAGES="e2e-up e2e-perf lib-aa lib" $RUN --stage preflight
    ;;
  perf)
    STAGES="e2e-up e2e-perf lib-aa lib" $RUN --stage preflight
    $RUN --stage e2e-up --stage e2e-perf --stage lib-aa --stage lib --stage verdict
    ;;
  full)
    STAGES="entry e2e-up lib-aa lib e2e-perf faults tenant ops existing-suites auth-attack" $RUN --stage preflight
    $RUN --stage selftest --stage calibrate \
         --stage entry --stage e2e-up \
         --stage e2e-perf --stage lib-aa --stage lib \
         --stage auth-attack --stage tenant --stage ops --stage faults \
         --stage existing-suites --stage scans --stage verdict
    ;;
  soak)
    : "${SOAK_RATE:?set SOAK_RATE to a plain number, e.g. SOAK_RATE=2.4 bash foundgine-perf.sh soak}"
    STAGES="soak" $RUN --stage preflight
    $RUN --stage soak --stage verdict
    ;;
  verdict)
    $RUN --stage verdict
    ;;
  *)
    sed -n 2,8p "$0"
    exit 2
    ;;
esac