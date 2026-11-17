#!/usr/bin/env bash
# R11: >= 24 h at 60% of the sustainable rate. SOAK_RATE must be set on purpose (0.6 x the rate e2e-perf found).
source "$(dirname "$0")/../lib.sh"; need docker || die "docker not found"; require_e2e
: "${SOAK_RATE:?set SOAK_RATE = 60% of the sustainable customer-to-cash rate from e2e-perf}"
: "${CRM_DB_PASSWORD:?needed by the post-soak invariant check}"
gen --soak-rate "$SOAK_RATE" --soak-hours "${SOAK_HOURS:-24}"; cd "$SUITE"
OUT=$WORK/e2e-soak; rm -rf "$OUT"; loader $PY -m fpbench journeys "$CFG/e2e-soak.json" --out "$OUT"
$PY -m fpbench check soak "$OUT" --dir "$EV" --min-hours "${MIN_SOAK_HOURS:-24}" || true
