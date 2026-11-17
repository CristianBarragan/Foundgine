#!/usr/bin/env bash
# R5: Hot Chocolate + EF vs Foundgine, claim-grade evidence. The equal-work controls (tuned EF, raw SQL floor, EF batch tool)
# are NOT built; until CONTROLS_DONE=1 R5 stays not_run even when the report is claim-grade, so nobody reads a pass as 'fair'.
source "$(dirname "$0")/../lib.sh"; source "$(dirname "$0")/_lib_stack.sh"; lib_prepare; cd "$SUITE"
OUT=$WORK/lib-ab; rm -rf "$OUT"
loader $PY -m fpbench run "$WORK/lib-ab.json" --out "$OUT"
$PY -m fpbench report "$OUT" --baseline hotchocolate --subject foundgine || true
if $PY -m fpbench check claim "$OUT/report.json" --dir "$EV"; then
  [ "${CONTROLS_DONE:-0}" = 1 ] || rec R5 not_run --detail "report is claim-grade but equal-work controls (tuned EF, raw SQL floor, EF batch tool) are not built; set CONTROLS_DONE=1 once they are" --artifact "$OUT/report.json"
fi
[ -f baselines/coffeebeanery.report.json ] && { $PY -m fpbench gate "$OUT/report.json" baselines/coffeebeanery.report.json --require-claim-grade || true; } \
  || say "no committed baseline: after a claim-grade run, copy $OUT/report.json to baselines/coffeebeanery.report.json"
