"""Regression gate: compare a new report.json with a committed baseline.
Fails (exit 1) when a cell's ratio CI lower bound drops by more than --tolerance, when a cell that
beat the baseline no longer excludes 1, or (with --require-claim-grade) when the report is not claim-grade."""
import json, sys
from .io import _rj

def compare(new, base, tolerance=0.10):
    problems = []
    nb = {(r["scenario"], r["concurrency"]): r for r in new["rows"]}
    for r in base["rows"]:
        k = (r["scenario"], r["concurrency"])
        if not r.get("ci"): continue
        n = nb.get(k)
        if not n or not n.get("ci"):
            problems.append(f"{k}: cell missing or without interval in new report"); continue
        if not n["equivalent"]:
            problems.append(f"{k}: responses no longer equivalent")
        if r["ci"]["lo"] > 1 and n["ci"]["lo"] <= 1:
            problems.append(f"{k}: advantage no longer significant (lo {n['ci']['lo']:.2f})")
        elif n["ci"]["lo"] < r["ci"]["lo"] * (1 - tolerance):
            problems.append(f"{k}: lower bound {n['ci']['lo']:.2f} < baseline {r['ci']['lo']:.2f} by more than {tolerance:.0%}")
    return problems

def main(argv):
    import argparse
    a = argparse.ArgumentParser(); a.add_argument("new"); a.add_argument("baseline")
    a.add_argument("--tolerance", type=float, default=0.10); a.add_argument("--require-claim-grade", action="store_true")
    a = a.parse_args(argv)
    new, base = _rj(a.new), _rj(a.baseline)
    probs = compare(new, base, a.tolerance)
    if a.require_claim_grade and not new["claim_grade"]:
        probs += ["report is not claim-grade: " + "; ".join(new["blockers"])]
    for p in probs: print("FAIL:", p)
    print("gate: " + ("FAILED" if probs else "passed"))
    return 1 if probs else 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
