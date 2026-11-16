#!/usr/bin/env bash
# R1: A/A run (same system under two names, 5+ sessions). Also the first real docker-sampler run (part of EG5).
source "$(dirname "$0")/../lib.sh"; source "$(dirname "$0")/_lib_stack.sh"; lib_prepare aa; cd "$SUITE"
OUT=$WORK/lib-aa; rm -rf "$OUT"
loader $PY -m fpbench run "$WORK/lib-aa.json" --out "$OUT"
$PY -m fpbench report "$OUT" --baseline hotchocolate --subject hotchocolate-b || true
$PY -m fpbench check aa "$OUT/report.json" --dir "$EV" || true
# EG5 needs three things: docker sampler + compose override (shown here), cgroup sampler, and the perf workflow run in CI.
$PY - "$OUT" "$EV" <<'P'
import glob, json, sys, subprocess
out, ev = sys.argv[1:3]
cells = [json.load(open(f)) for f in glob.glob(f"{out}/session-*/*/*/*.json") if not f.endswith(".samples.jsonl.gz")]
good = [c for c in cells if c["resources"].get("declared") and c["resources"].get("valid") and c["resources"].get("cpu_cores_mean") is not None]
msg = f"docker sampler produced valid telemetry in {len(good)}/{len(cells)} cells"
st = "not_run" if good else "fail"
extra = "; cgroup mode and the perf CI workflow still have to run once (PERF_WORKFLOW_RAN=1 + CGROUP_RAN=1 to record)"
import os
if good and os.environ.get("PERF_WORKFLOW_RAN") == "1" and os.environ.get("CGROUP_RAN") == "1": st, extra = "pass", ""
subprocess.run([sys.executable, "-m", "fpbench", "evidence", "EG5", st, "--dir", ev, "--source", "auto", "--detail", msg + extra], check=True)
P
