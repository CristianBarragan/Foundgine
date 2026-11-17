import argparse, asyncio, json, os, sys
from . import runner, report
from .io import _rj

DEFAULT_CRITERIA = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "readiness", "criteria.json")

def main():
    p = argparse.ArgumentParser(prog="fpbench")
    sub = p.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("run"); r.add_argument("config"); r.add_argument("--out", required=True)
    r.add_argument("--sessions", type=int); r.add_argument("--seed", type=int)
    g = sub.add_parser("report"); g.add_argument("out"); g.add_argument("--baseline", required=True); g.add_argument("--subject", required=True)
    j = sub.add_parser("journeys"); j.add_argument("config"); j.add_argument("--out", required=True)
    j.add_argument("--sessions", type=int); j.add_argument("--seed", type=int)
    jr = sub.add_parser("journey-report"); jr.add_argument("out"); jr.add_argument("--slo-p99-s", type=float)
    gt = sub.add_parser("gate"); gt.add_argument("new"); gt.add_argument("baseline"); gt.add_argument("--tolerance", type=float, default=0.10); gt.add_argument("--require-claim-grade", action="store_true")
    # readiness
    ev = sub.add_parser("evidence", help="record evidence for a criterion (manual or external results)")
    ev.add_argument("id"); ev.add_argument("status", choices=["pass", "fail", "not_run", "accepted_gap"]); ev.add_argument("--dir", required=True)
    ev.add_argument("--measured"); ev.add_argument("--threshold", default=""); ev.add_argument("--detail", default=""); ev.add_argument("--owner")
    ev.add_argument("--source", default="manual"); ev.add_argument("--artifact", action="append", default=[])
    rd = sub.add_parser("readiness", help="compute the verdict from an evidence directory"); rd.add_argument("dir"); rd.add_argument("--criteria", default=DEFAULT_CRITERIA)
    ck = sub.add_parser("check", help="evaluate raw results into readiness evidence"); ck.add_argument("what", choices=["aa", "soak", "faults", "tenant", "calibrate", "perf", "claim", "restart", "recovery", "parity", "authattack"])
    ck.add_argument("path"); ck.add_argument("--dir", required=True); ck.add_argument("--id"); ck.add_argument("--prefix"); ck.add_argument("--require", default="")
    ck.add_argument("--max-drain-s", type=float); ck.add_argument("--vectors"); ck.add_argument("--endpoint", action="append", default=[]); ck.add_argument("--max-recovery-s", type=float, default=120); ck.add_argument("--max-error-rate", type=float, default=0.001); ck.add_argument("--min-hours", type=float, default=24.0); ck.add_argument("--rate", type=float); ck.add_argument("--duration", type=float, default=30)
    ck.add_argument("--min-journeys", type=int, default=1000); ck.add_argument("--slo-p99-s", type=float); ck.add_argument("--target-rate", type=float)
    sp = sub.add_parser("schemaparity", help="each arm's schema must come from its own migration tool, and arms of one runtime must have the same schema")
    sp.add_argument("config"); sp.add_argument("--out", required=True)
    az = sub.add_parser("armauthz", help="all arms of a group must make identical Keycloak authn/authz decisions and return identical rows")
    az.add_argument("config"); az.add_argument("--vectors", required=True); az.add_argument("--out", required=True)
    iv = sub.add_parser("invariants"); iv.add_argument("rest", nargs=argparse.REMAINDER)
    a = p.parse_args()
    if a.cmd == "gate":
        from . import gate
        args = [a.new, a.baseline, "--tolerance", str(a.tolerance)] + (["--require-claim-grade"] if a.require_claim_grade else [])
        raise SystemExit(gate.main(args))
    if a.cmd == "invariants":
        from . import invariants; raise SystemExit(invariants.main(a.rest))
    if a.cmd == "armauthz":
        from . import armauthz
        rep = armauthz.run(a.config, a.vectors, a.out)
        print("armauthz:", "PASS" if rep["pass"] else "FAIL"); [print(" -", f) for f in rep["failures"]]
        raise SystemExit(0 if rep["pass"] else 1)
    if a.cmd == "schemaparity":
        from . import schemaparity
        rep = schemaparity.run(a.config, a.out)
        print("schemaparity:", "PASS" if rep["pass"] else "FAIL"); [print(" -", f) for f in rep["failures"]]
        raise SystemExit(0 if rep["pass"] else 1)
    if a.cmd == "evidence":
        from . import readiness
        m = a.measured
        try: m = json.loads(m) if m else None
        except ValueError: pass
        e = readiness.record(a.dir, a.id, a.status, m, a.threshold, a.detail, a.source, a.artifact, a.owner); print(f"{e['id']}: {e['status']}"); return
    if a.cmd == "readiness":
        from . import readiness
        v = readiness.verdict(_rj(a.criteria), a.dir)
        print(v["verdict"]); [print(" -", r) for r in v["reasons"]]
        print(f"pass={v['counts']['pass']} fail={v['counts']['fail']} gap={v['counts']['accepted_gap']} not_run={v['counts']['not_run']}  ->  {a.dir}/verdict.md")
        raise SystemExit(0 if v["verdict"].startswith("PRODUCTION") else 1)
    if a.cmd == "check":
        from . import checks
        req = [x for x in a.require.split(",") if x]
        if a.what == "aa": e = checks.aa(a.path, a.dir, cid=a.id or "R1")
        elif a.what == "soak": e = checks.soak(a.path, a.dir, min_hours=a.min_hours, cid=a.id or "R11")
        elif a.what == "tenant": e = checks.tenant(a.path, a.dir, min_journeys=a.min_journeys, cid=a.id or "R12")
        elif a.what == "perf": e = checks.perf(a.path, a.dir, a.slo_p99_s, a.target_rate, cid=a.id or "R4")
        elif a.what == "restart": e = checks.restart(a.path, a.dir, a.max_error_rate, cid=a.id or "R17")
        elif a.what == "recovery": e = checks.recovery(a.path, a.dir, a.slo_p99_s, a.max_recovery_s, cid=a.id or "R6")
        elif a.what == "parity":
            from . import parity; e = parity.run(a.path, a.vectors, a.dir, cid=a.id or "R13")
        elif a.what == "authattack":
            from . import authattack; e = authattack.run(a.path, a.dir, a.endpoint, cid=a.id or "R22")
        elif a.what == "claim": e = checks.claim(a.path, a.dir, cid=a.id or "R5")
        elif a.what == "calibrate": e = checks.calibrate(a.path, a.dir, rate=a.rate, duration=a.duration, cid=a.id or "R3")
        else: e = checks.faults(a.path, a.dir, a.id, a.prefix, req, a.max_drain_s)
        print(f"{e['id']}: {e['status']} {e['measured'] if e['measured'] else ''} {e['detail']}"); raise SystemExit(0 if e["status"] == "pass" else 1)
    if a.cmd == "journeys":
        from . import journey
        asyncio.run(journey.run_all(a.config, a.out, a.sessions, a.seed)); return
    if a.cmd == "journey-report":
        from . import journey_report
        rep = journey_report.build(a.out, a.slo_p99_s); print(f"claim_grade={rep['claim_grade']}"); [print(" -", b) for b in rep["blockers"]]; return
    if a.cmd == "run":
        asyncio.run(runner.run_all(a.config, a.out, a.sessions, a.seed))
    else:
        rep = report.build(a.out, a.baseline, a.subject)
        print(f"claim_grade={rep['claim_grade']}"); [print(" -", b) for b in rep["blockers"]]
main()
