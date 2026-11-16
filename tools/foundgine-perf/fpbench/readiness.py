"""Production-readiness evidence ledger and verdict.

Every criterion in readiness/criteria.json ends in exactly one state: pass, fail, accepted_gap or not_run.
A criterion nobody has produced evidence for is `not_run`, never silently skipped, and `not_run` never counts as a pass.
The verdict follows docs/PRODUCTION-READINESS-PLAN.md section 6 literally.
"""
import glob, json, os, time
from .io import _rj, _wj, _wt

STATUSES = ("pass", "fail", "accepted_gap", "not_run")

def record(evdir, cid, status, measured=None, threshold=None, detail="", source="auto", artifacts=None, owner=None):
    if status not in STATUSES:
        raise ValueError(f"status must be one of {STATUSES}")
    if status == "accepted_gap" and not (owner and detail):
        raise ValueError("accepted_gap needs --owner and --detail (who accepted the gap and why)")
    os.makedirs(evdir, exist_ok=True)
    ev = {"id": cid, "status": status, "measured": measured, "threshold": threshold, "detail": detail,
          "source": source, "artifacts": artifacts or [], "owner": owner, "recorded_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    tmp = os.path.join(evdir, f".{cid}.tmp"); _wj(tmp, ev); os.replace(tmp, os.path.join(evdir, f"{cid}.json"))
    return ev

def _evidence(evdir):
    out = {}
    for f in glob.glob(os.path.join(evdir, "*.json")):
        if os.path.basename(f) in ("verdict.json",): continue
        try:
            e = _rj(f)
            if isinstance(e, dict) and "id" in e and "status" in e: out[e["id"]] = e
        except ValueError: pass
    return out

def verdict(criteria, evdir):
    os.makedirs(evdir, exist_ok=True)
    ev = _evidence(evdir)
    def merged(items, kind):
        rows = []
        for c in items:
            e = ev.get(c["id"], {})
            st = e.get("status", "not_run")
            rows.append({**c, "kind": kind, "status": st, "measured": e.get("measured"), "evidence_detail": e.get("detail", ""),
                         "source": e.get("source"), "owner": e.get("owner"), "recorded_at": e.get("recorded_at")})
        return rows
    eg, rows, ind = merged(criteria["entry_gate"], "entry"), merged(criteria["rows"], "row"), merged(criteria["independence"], "independence")
    ok = lambda r: r["status"] == "pass"
    reasons = []
    eg_ok = all(ok(r) for r in eg)
    if not eg_ok: reasons.append("entry gate not passed: " + ", ".join(r["id"] for r in eg if not ok(r)))
    critical = [r for r in rows if r.get("tier") == "critical"]
    must = [r for r in rows if r.get("tier") in ("critical", "must")]
    bad_critical = [r for r in critical if not ok(r)]
    if bad_critical: reasons.append("critical rows not passed: " + ", ".join(f"{r['id']}({r['status']})" for r in bad_critical))
    all_rows_pass = all(ok(r) for r in rows)
    ind_required = [r for r in ind if r.get("required")]
    ind_ok = all(ok(r) for r in ind_required)
    gaps_ok = all(ok(r) or r["status"] == "accepted_gap" for r in rows if r.get("tier") not in ("critical", "must"))
    if eg_ok and all_rows_pass and ind_ok:
        v = "PRODUCTION GRADE for the named scope"
    elif eg_ok and not bad_critical and all(ok(r) for r in must) and gaps_ok:
        v = "CONDITIONALLY READY"
        reasons += [f"{r['id']} accepted gap ({r['owner']}): {r['evidence_detail']}" for r in rows if r["status"] == "accepted_gap"]
        reasons += [f"independence not met: {r['id']}" for r in ind_required if not ok(r)]
    else:
        v = "NOT READY"
        if eg_ok and not bad_critical:
            reasons.append("must-pass rows not passed: " + ", ".join(r["id"] for r in must if not ok(r)) if not all(ok(r) for r in must) else "non-critical rows have no accepted gap: " + ", ".join(r["id"] for r in rows if r.get("tier") not in ("critical", "must") and not (ok(r) or r["status"] == "accepted_gap")))
    counts = {s: sum(r["status"] == s for r in eg + rows + ind) for s in STATUSES}
    out = {"verdict": v, "reasons": reasons, "counts": counts, "entry_gate": eg, "rows": rows, "independence": ind,
           "outstanding": [{"id": r["id"], "status": r["status"], "how": r.get("how"), "automation": r.get("automation")}
                           for r in eg + rows + ind if r["status"] in ("not_run", "fail") and (r["kind"] != "independence" or r.get("required"))]}
    _wj(os.path.join(evdir, "verdict.json"), out); _wt(os.path.join(evdir, "verdict.md"), render(out))
    return out

def render(v):
    icon = {"pass": "PASS", "fail": "FAIL", "not_run": "NOT RUN", "accepted_gap": "GAP (accepted)"}
    L = ["# Production-readiness verdict", "", f"**{v['verdict']}**", ""]
    c = v["counts"]; L += [f"Pass {c['pass']} | Fail {c['fail']} | Accepted gap {c['accepted_gap']} | Not run {c['not_run']}", ""]
    if v["reasons"]: L += ["## Why"] + [f"- {r}" for r in v["reasons"]] + [""]
    for title, key in (("Entry gate", "entry_gate"), ("Criteria", "rows"), ("Independence", "independence")):
        L += [f"## {title}", "", "| ID | Area / item | Result | Measured | Threshold | Automation |", "|---|---|---|---|---|---|"]
        for r in v[key]:
            a = (r.get("automation") or {}).get("level", "")
            L.append(f"| {r['id']} | {r.get('area', '')} {r['title']} | {icon[r['status']]} | {json.dumps(r['measured']) if r['measured'] is not None else ''} | {r.get('threshold', '')} | {a} |")
        L.append("")
    if v["outstanding"]:
        L += ["## Outstanding (nothing is hidden: each item says how to close it)", ""]
        L += [f"- **{o['id']}** ({o['status']}): {o['how']}" for o in v["outstanding"]]
    return "\n".join(L) + "\n"
