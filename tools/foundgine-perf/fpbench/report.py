"""Aggregate sessions into a claim-grade (or explicitly NOT claim-grade) report."""
from .io import _wj, _wt, _rj
import glob, json, os, statistics
from collections import defaultdict
from .stats import bootstrap_ratio_ci

MIN_SESSIONS = 5

def load(outdir):
    cells = []
    for f in sorted(glob.glob(os.path.join(outdir, "session-*", "*", "*", "*.json"))):
        if f.endswith(".samples.jsonl.gz"):
            continue
        r = _rj(f); r["session"] = f.split(os.sep)[-4]; cells.append(r)
    return cells

def build(outdir, baseline, subject):
    cfg = _rj(os.path.join(outdir, "config.json"))
    envd = _rj(os.path.join(outdir, "environment.json"))
    eq = _rj(os.path.join(outdir, "equivalence.json"))
    cells = load(outdir)
    by = defaultdict(lambda: defaultdict(list))   # (scenario,mode,key) -> target -> [cell]
    for c in cells:
        key = (c["scenario"], c["mode"], c["concurrency"] if c["mode"] == "closed" else round(c["rate"]))
        by[key][c["target"]].append(c)
    sessions = len({c["session"] for c in cells})
    rows, notes = [], []
    for (sc, mode, k), t in sorted(by.items(), key=lambda x: (x[0][0], x[0][1], x[0][2])):
        if mode != "closed":
            continue
        a = [c["logical_rps"] for c in t.get(subject, []) if c["valid"]]
        b = [c["logical_rps"] for c in t.get(baseline, []) if c["valid"]]
        ci = bootstrap_ratio_ci(a, b) if len(a) >= 2 and len(b) >= 2 else None
        p99 = lambda lst: statistics.median([c["service_ms"]["p99"] for c in lst if c["valid"]]) if any(c["valid"] for c in lst) else None
        res = lambda lst: [c["resources"] for c in lst if c["valid"] and c["resources"].get("declared")]
        def cpu(lst):
            v = [r["cpu_cores_mean"] for r in res(lst) if "cpu_cores_mean" in r]; return statistics.median(v) if v else None
        def rss(lst):
            v = [r["rss_mb_max"] for r in res(lst) if "rss_mb_max" in r]; return statistics.median(v) if v else None
        rows.append({"scenario": sc, "concurrency": k, "equivalent": eq.get(sc, {}).get("equivalent"),
                     "baseline_rps": statistics.median(b) if b else None, "subject_rps": statistics.median(a) if a else None,
                     "ci": ci, "valid_sessions": (len(a), len(b)),
                     "p99_baseline": p99(t.get(baseline, [])), "p99_subject": p99(t.get(subject, [])),
                     "cpu_baseline": cpu(t.get(baseline, [])), "cpu_subject": cpu(t.get(subject, [])),
                     "rss_baseline": rss(t.get(baseline, [])), "rss_subject": rss(t.get(subject, []))})
    grade = []
    if sessions < MIN_SESSIONS: grade.append(f"only {sessions} independent sessions (need >= {MIN_SESSIONS})")
    if not envd.get("disclosure_complete"): grade.append("environment disclosure incomplete: " + ", ".join(envd["missing"]))
    if any(not v["equivalent"] for v in eq.values()): grade.append("response equivalence failed for at least one scenario")
    if any(r["ci"] is None for r in rows): grade.append("some cells lack enough valid sessions for an interval")
    if not any(c["resources"].get("declared") for c in cells): grade.append("no resource telemetry declared")
    if any((not c["valid"]) for c in cells): grade.append(f"{sum(not c['valid'] for c in cells)} cells invalid (see invalid_reasons)")
    open_cells = [c for c in cells if c["mode"] == "open"]
    if not open_cells: grade.append("no open-loop run (coordinated omission not addressed)")
    report = {"baseline": baseline, "subject": subject, "sessions": sessions, "claim_grade": not grade,
              "blockers": grade, "rows": rows, "environment": envd, "equivalence": eq,
              "open_loop": [{"target": c["target"], "scenario": c["scenario"], "session": c["session"], "rate": c["rate"],
                             "throughput": c["throughput_rps"], "p99_from_intended_ms": c["latency_from_intended_ms"].get("p99"),
                             "errors": c["errors"]} for c in open_cells]}
    _wj(os.path.join(outdir, "report.json"), report)
    _wt(os.path.join(outdir, "report.md"), render(report))
    return report

def _f(x, spec=".0f"):
    return "n/a" if x is None else format(x, spec)

def render(r):
    L = [f"# Performance report: {r['subject']} vs {r['baseline']}", "",
         f"**Claim grade: {'YES' if r['claim_grade'] else 'NO'}** ({r['sessions']} independent sessions)", ""]
    if r["blockers"]:
        L += ["## Blockers (why this is not claim-grade)"] + [f"- {b}" for b in r["blockers"]] + [""]
    L += ["## Closed-loop throughput (median across sessions, 95% bootstrap CI of the ratio)", "",
          "| Scenario | C | Equiv | Baseline ops/s | Subject ops/s | Ratio [95% CI] | p99 base/subj (ms) | CPU cores base/subj | Peak RSS MB base/subj |",
          "|---|--:|:-:|--:|--:|---|---|---|---|"]
    for x in r["rows"]:
        ci = x["ci"]
        ratio = "n/a" if not ci else f"{ci['ratio']:.2f}x [{ci['lo']:.2f}, {ci['hi']:.2f}]" + ("" if ci["excludes_1"] else " (includes 1)")
        L.append(f"| {x['scenario']} | {x['concurrency']} | {'yes' if x['equivalent'] else 'NO'} | {_f(x['baseline_rps'])} | {_f(x['subject_rps'])} | {ratio} | "
                 f"{_f(x['p99_baseline'],'.1f')}/{_f(x['p99_subject'],'.1f')} | {_f(x['cpu_baseline'],'.2f')}/{_f(x['cpu_subject'],'.2f')} | {_f(x['rss_baseline'])}/{_f(x['rss_subject'])} |")
    if r["open_loop"]:
        L += ["", "## Open-loop (latency from intended send time)", "", "| Session | Scenario | Target | Offered rps | Achieved rps | p99 from intended (ms) | Errors |", "|---|---|---|--:|--:|--:|--:|"]
        for o in r["open_loop"]:
            L.append(f"| {o['session']} | {o['scenario']} | {o['target']} | {o['rate']:.0f} | {o['throughput']:.0f} | {_f(o['p99_from_intended_ms'],'.1f')} | {o['errors']} |")
    e = r["environment"]
    L += ["", "## Environment", "", f"- CPU: {e.get('cpu_model')} x {e.get('logical_cores')} logical cores, RAM {e.get('ram_gb')} GB", f"- OS: {e.get('os')}", f"- Tools: {json.dumps(e.get('tools'))}", f"- Commit: {e.get('git_commit')}", ""]
    return "\n".join(L)
