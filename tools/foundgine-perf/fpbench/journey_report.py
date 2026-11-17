"""Aggregate journey sessions: sustainable rate, step breakdown, fault drain, claim-grade blockers."""
from .io import _wj, _wt, _rj
import glob, json, os, statistics
from collections import defaultdict

def build(outdir, slo_p99_s=None, max_fail=0.01, min_completion=0.98):
    envd = _rj(os.path.join(outdir, "environment.json"))
    cells = []
    for f in sorted(glob.glob(os.path.join(outdir, "session-*", "journeys", "*.json"))):
        c = _rj(f); c["session"] = f.split(os.sep)[-3]; cells.append(c)
    g = defaultdict(list)
    for c in cells: g[(c["journey"], c["mode"], c["level"])].append(c)
    rows = []
    for (j, mode, lvl), cs in sorted(g.items()):
        med = lambda xs: statistics.median(xs) if xs else None
        ok = [c for c in cs if c["valid"]]
        fail = med([(c["failed"] + c["incomplete"]) / max(1, c["offered"]) for c in cs])
        comp = med([c["completed"] / max(1, c["offered"]) for c in cs])
        p = lambda k: med([c["e2e_from_intended_ms"].get(k) for c in ok if c["e2e_from_intended_ms"].get(k) is not None])
        steps = defaultdict(list)
        for c in ok:
            for k, v in c["steps_ms"].items(): steps[k].append(v["p50"])
        row = {"journey": j, "mode": mode, "level": lvl, "sessions": len(cs), "valid_sessions": len(ok),
               "failure_rate": fail, "completion": comp, "p50_s": (p("p50") or 0) / 1000, "p95_s": (p("p95") or 0) / 1000,
               "p99_s": (p("p99") or 0) / 1000, "steps_p50_ms": {k: med(v) for k, v in steps.items()},
               "probe_max": {n: med([c["probes"][n]["max"] for c in ok if c["probes"].get(n, {}).get("samples")]) for n in (ok[0]["probes"] if ok else {})},
               "cpu": {n: med([c["resources"][n]["cpu_cores_mean"] for c in ok if "cpu_cores_mean" in c["resources"].get(n, {})]) for n in (ok[0]["resources"] if ok else {})},
               "rss_mb": {n: med([c["resources"][n]["rss_mb_max"] for c in ok if "rss_mb_max" in c["resources"].get(n, {})]) for n in (ok[0]["resources"] if ok else {})},
               "fault": med([c["fault"]["time_to_drain_s"] for c in ok if "fault" in c]) if any("fault" in c for c in ok) else None}
        row["sustainable"] = (mode == "open" and fail is not None and fail <= max_fail and (comp or 0) >= min_completion
                              and (slo_p99_s is None or row["p99_s"] <= slo_p99_s))
        rows.append(row)
    sustain = {}
    for r in rows:
        if r["sustainable"]: sustain[r["journey"]] = max(sustain.get(r["journey"], 0), r["level"])
    sessions = len({c["session"] for c in cells})
    blockers = []
    if sessions < 5: blockers.append(f"only {sessions} independent sessions (need >= 5)")
    if not envd.get("disclosure_complete"): blockers.append("environment disclosure incomplete: " + ", ".join(envd["missing"]))
    if not any(c["resources"] for c in cells): blockers.append("no resource telemetry declared")
    if not any(c["probes"] for c in cells): blockers.append("no outbox/queue probes declared")
    if slo_p99_s is None: blockers.append("no p99 SLO supplied: sustainable rate uses failure/completion only")
    inv = sum(not c["valid"] for c in cells)
    if inv: blockers.append(f"{inv} cells invalid")
    rep = {"sessions": sessions, "claim_grade": not blockers, "blockers": blockers, "rows": rows, "sustainable_rate": sustain, "environment": envd}
    _wj(os.path.join(outdir, "journey-report.json"), rep)
    L = [f"# Journey report", "", f"**Claim grade: {'YES' if rep['claim_grade'] else 'NO'}** ({sessions} sessions)", ""]
    if blockers: L += ["## Blockers"] + [f"- {b}" for b in blockers] + [""]
    L += ["## Sustainable open-loop rate (journeys/s)", ""] + [f"- {k}: {v:g}" for k, v in sustain.items()] + (["- none sustained"] if not sustain else []) + [""]
    L += ["## Cells (medians across valid sessions; latency from intended arrival)", "",
          "| Journey | Mode | Level | Fail | Completion | p50 s | p95 s | p99 s | Sustainable | Fault drain s |", "|---|---|--:|--:|--:|--:|--:|--:|:-:|--:|"]
    for r in rows:
        L.append(f"| {r['journey']} | {r['mode']} | {r['level']:g} | {r['failure_rate']:.1%} | {r['completion']:.1%} | {r['p50_s']:.2f} | {r['p95_s']:.2f} | {r['p99_s']:.2f} | "
                 f"{'yes' if r['sustainable'] else 'no'} | {'' if r['fault'] is None else format(r['fault'], '.1f')} |")
    L += ["", "## Where the time goes (median step ms)", ""]
    for r in rows:
        if r["steps_p50_ms"]: L.append(f"- {r['journey']} {r['mode']}={r['level']:g}: " + ", ".join(f"{k} {v:.0f}" for k, v in sorted(r["steps_p50_ms"].items(), key=lambda x: -x[1])[:6]))
    _wt(os.path.join(outdir, "journey-report.md"), "\n".join(L))
    return rep
