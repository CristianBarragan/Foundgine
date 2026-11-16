#!/usr/bin/env bash
# R7 (kill singles+pairs), R8 (net latency/partition), R9 (crash x3 mid-flight), R10 (drain time). ~3 h at FAULT_SESSIONS=2.
source "$(dirname "$0")/../lib.sh"; need docker || die "docker not found"; require_e2e
: "${CRM_DB_PASSWORD:?the invariant checks call sqlcmd with \$CRM_DB_PASSWORD (the SA password of the CRM database container)}"
gen --fault-sessions "${FAULT_SESSIONS:-2}"; cd "$SUITE"
OUT=$WORK/e2e-faults; rm -rf "$OUT"; $PY -m fpbench journeys "$CFG/e2e-faults.json" --out "$OUT"
req() { $PY readiness/gen_configs.py --out /tmp/gen-list --list-cells "$1" | paste -sd, -; }
$PY -m fpbench check faults "$OUT" --dir "$EV" --id R7  --prefix kill-  --require "$(req kill-)"  || true
$PY -m fpbench check faults "$OUT" --dir "$EV" --id R8  --prefix net-   --require "$(req net-)"   || true
$PY -m fpbench check faults "$OUT" --dir "$EV" --id R9  --prefix crash- --require "$(req crash-)" || true
$PY -m fpbench check faults "$OUT" --dir "$EV" --id R10 --prefix kill-  --require "$(req kill-)" --max-drain-s "${MAX_DRAIN_S:-120}" || true
