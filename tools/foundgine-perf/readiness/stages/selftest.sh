#!/usr/bin/env bash
# R2 (gate catches an injected regression) + the whole harness on mock servers. No Docker needed.
source "$(dirname "$0")/../lib.sh"; cd "$SUITE"
say "unit tests"; $PY -W error::ResourceWarning -m unittest discover -s tests >/dev/null
say "A/B selftest"; bash selftest/run_selftest.sh >"$WORK/selftest.log" 2>&1 || die "selftest failed, see $WORK/selftest.log"
say "journey selftest + fault pipeline"; bash selftest/run_selftest_journeys.sh >"$WORK/selftest-journeys.log" 2>&1 || die "journey selftest failed, see $WORK/selftest-journeys.log"
$PY -m fpbench check faults /tmp/jt --dir "$WORK/selftest-ev" --id MOCK --prefix c2c- --require fault >&2 || die "fault evaluator rejected the mock fault cell"
say "inject a regression and require the gate to fail"
$PY - <<'P'
import json
r = json.load(open("/tmp/st-ok/report.json"))
assert any(x.get("ci") and x["ci"]["lo"] > 1 for x in r["rows"]), "mock A/B run has no significant advantage to regress"
json.dump(r, open("/tmp/gate-same.json", "w"))
for x in r["rows"]:
    if x.get("ci"): x["ci"]["lo"] *= 0.4; x["ci"]["ratio"] *= 0.4
json.dump(r, open("/tmp/gate-regressed.json", "w"))
P
$PY -m fpbench gate /tmp/gate-same.json /tmp/st-ok/report.json >/dev/null || { rec R2 fail --detail "gate rejected an identical report"; exit 1; }
if $PY -m fpbench gate /tmp/gate-regressed.json /tmp/st-ok/report.json >/dev/null; then rec R2 fail --detail "gate accepted a 60% regression"; exit 1; fi
rec R2 pass --measured '{"identical":"passed","injected_regression":"rejected"}' --threshold "fpbench gate exits non-zero" --detail "verified on mock servers"
