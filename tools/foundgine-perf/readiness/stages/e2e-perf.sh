#!/usr/bin/env bash
# R4 (and R6 data with --overload). Needs the stack from `e2e-up` (or any stack with this compose project name).
source "$(dirname "$0")/../lib.sh"; need docker || die "docker not found"; require_e2e; gen --sessions "${SESSIONS:-5}"; cd "$SUITE"
if [ "${OVERLOAD:-0}" = 1 ]; then
  OUT=$WORK/e2e-overload; rm -rf "$OUT"; $PY -m fpbench journeys "$CFG/e2e-overload.json" --out "$OUT" --sessions 1
  $PY -m fpbench journey-report "$OUT" ${SLO_P99_S:+--slo-p99-s "$SLO_P99_S"} || true
  S=$WORK/e2e-staged; rm -rf "$S"; $PY -m fpbench journeys "$CFG/e2e-staged.json" --out "$S" --sessions 1
  if [ -n "${SLO_P99_S:-}" ]; then $PY -m fpbench check recovery "$S" --dir "$EV" --slo-p99-s "$SLO_P99_S" --max-recovery-s "${MAX_RECOVERY_S:-120}" || true
  else rec R6 not_run --detail "staged overload/recovery ran ($S) but SLO_P99_S is not set"; fi
  exit 0
fi
OUT=$WORK/e2e-perf; rm -rf "$OUT"; loader $PY -m fpbench journeys "$CFG/e2e-perf.json" --out "$OUT"
$PY -m fpbench journey-report "$OUT" ${SLO_P99_S:+--slo-p99-s "$SLO_P99_S"} || true
if [ -n "${SLO_P99_S:-}" ] && [ -n "${TARGET_RATE:-}" ]; then $PY -m fpbench check perf "$OUT" --dir "$EV" --slo-p99-s "$SLO_P99_S" --target-rate "$TARGET_RATE" || true
else rec R4 not_run --detail "run finished ($OUT) but SLO_P99_S and TARGET_RATE were not set, so no target to judge against"; fi
