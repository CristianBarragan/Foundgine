import argparse, json, os, subprocess, sys, tempfile, unittest
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, ROOT); sys.path.insert(0, os.path.join(ROOT, "readiness"))
import gen_configs, parse_results
from fpbench import checks, readiness

TRX = """<?xml version="1.0"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{res}</Results><TestDefinitions>{defs}</TestDefinitions></TestRun>"""
def _load(p):
    with open(p) as f: return json.load(f)
def _dump(o, p):
    with open(p, "w") as f: json.dump(o, f)

def trx(outcomes):
    res = "".join(f'<UnitTestResult executionId="e{i}" outcome="{o}"/>' for i, (a, o) in enumerate(outcomes))
    defs = "".join(f'<UnitTest name="t{i}"><Execution id="e{i}"/><TestMethod codeBase="/b/{a}.dll"/></UnitTest>' for i, (a, o) in enumerate(outcomes))
    return TRX.format(res=res, defs=defs)

class Parse(unittest.TestCase):
    def d(self, name, text):
        d = tempfile.mkdtemp()
        with open(os.path.join(d, name), "w") as f: f.write(text)
        return d
    def test_trx_counts_per_assembly(self):
        d = self.d("a.trx", trx([("Redis.Tests", "Passed"), ("Redis.Tests", "Passed"), ("Core.Tests", "Passed")]))
        data = parse_results.trx(d); self.assertEqual(data["Redis.Tests.dll"]["passed"], 2)
        self.assertTrue(parse_results.totals("trx", data)["ok"])
        self.assertEqual(parse_results.matching("trx", data, "redis"), ["Redis.Tests.dll"])
    def test_skipped_is_rejected(self):
        t = parse_results.totals("trx", parse_results.trx(self.d("a.trx", trx([("A", "Passed"), ("A", "NotExecuted")]))))
        self.assertFalse(t["ok"]); self.assertEqual(t["skipped"], 1)
    def test_zero_tests_is_not_ok(self):
        self.assertFalse(parse_results.totals("trx", {})["ok"]); self.assertFalse(parse_results.totals("surefire", {})["ok"])
    def test_surefire(self):
        d = self.d("TEST-x.RedisTest.xml", '<testsuite name="x.RedisTest" tests="3" failures="0" errors="0" skipped="0"/>')
        data = parse_results.surefire(d); self.assertTrue(parse_results.totals("surefire", data)["ok"])
        self.assertEqual(parse_results.matching("surefire", data, "redis"), ["x.RedisTest"])
        d2 = self.d("TEST-y.xml", '<testsuite name="y" tests="3" failures="0" errors="1" skipped="1"/>')
        self.assertFalse(parse_results.totals("surefire", parse_results.surefire(d2))["ok"])

class Gen(unittest.TestCase):
    def args(self, **kw):
        a = gen_configs.parser().parse_args([]); a.out = tempfile.mkdtemp()
        for k, v in kw.items(): setattr(a, k, v)
        return a
    def test_names_follow_project(self):
        a = self.args(project="p1"); out = gen_configs.build(a)
        f = _load(os.path.join(out, "e2e-faults.json"))
        self.assertEqual(len(f["journeys"]), 8 + 5 + 12 + 6)
        self.assertEqual(f["journeys"][0]["events"][0]["cmd"], "docker kill p1-crm-api-1")
        self.assertEqual(f["sessions"], 2)
        self.assertNotIn("foundgine-e2e", json.dumps(f).replace("foundgine-e2e-example", ""))
    def test_partition_reconnects_with_service_alias(self):
        out = gen_configs.build(self.args()); f = _load(os.path.join(out, "e2e-faults.json"))
        j = next(x for x in f["journeys"] if x["name"] == "net-partition-keycloak")
        self.assertIn("--alias keycloak", j["events"][1]["cmd"]); self.assertIn("--alias keycloak", j["cleanup_cmd"])
    def test_every_fault_cell_checks_invariants_and_cleans_up(self):
        out = gen_configs.build(self.args()); f = _load(os.path.join(out, "e2e-faults.json"))
        for j in f["journeys"]:
            self.assertTrue(j["post_cmd"] and j["cleanup_cmd"] and j["reset_cmd"], j["name"])
            names = [e["name"] for e in j["events"]]; self.assertIn("fault_start", names); self.assertIn("fault_end", names)
    def test_list_cells(self):
        n = gen_configs.cell_names(self.args()); self.assertIn("kill-pair-erp+temporal", n); self.assertIn("crash-erp-db", n)

def cell(name, **kw):
    c = {"journey": name, "valid": True, "invalid_reasons": [], "offered": 100, "completed": 100, "failed": 0, "incomplete": 0,
         "post_checks": [{"name": "invariants", "rc": 0}], "fault": {"time_to_drain_s": 10}, "failure_reasons": {}}
    c.update(kw); return c
def results(cells):
    d = tempfile.mkdtemp()
    for s, cs in enumerate(cells):
        os.makedirs(os.path.join(d, f"session-{s+1:02d}", "journeys"))
        for c in cs: _dump(c, os.path.join(d, f"session-{s+1:02d}", "journeys", c["journey"] + ".json"))
    return d

class Faults(unittest.TestCase):
    def ev(self): return tempfile.mkdtemp()
    def test_missing_cell_is_not_run_never_pass(self):
        e = checks.faults(results([[cell("kill-erp")]]), self.ev(), "R7", "kill-", ["erp", "crm-api"]); self.assertEqual(e["status"], "not_run")
    def test_invariant_failure_in_any_session_fails(self):
        bad = cell("kill-erp", post_checks=[{"name": "invariants", "rc": 1}], valid=False, invalid_reasons=["post-check invariants failed"])
        e = checks.faults(results([[cell("kill-erp")], [bad]]), self.ev(), "R7", "kill-", ["erp"]); self.assertEqual(e["status"], "fail")
    def test_cell_without_postchecks_fails(self):
        e = checks.faults(results([[cell("kill-erp", post_checks=[])]]), self.ev(), "R7", "kill-", ["erp"]); self.assertEqual(e["status"], "fail")
    def test_drain_limit(self):
        r = results([[cell("kill-erp", fault={"time_to_drain_s": 500})]])
        self.assertEqual(checks.faults(r, self.ev(), "R10", "kill-", ["erp"], 120)["status"], "fail")
        self.assertEqual(checks.faults(r, self.ev(), "R10", "kill-", ["erp"], 600)["status"], "pass")
    def test_all_present_passes(self):
        self.assertEqual(checks.faults(results([[cell("kill-erp"), cell("kill-pair-a+b")]]), self.ev(), "R7", "kill-", ["erp", "pair-a+b"])["status"], "pass")
    def test_restart_error_rate(self):
        ok = results([[cell("graceful-restart-crm-api", failed=0)]]); self.assertEqual(checks.restart(ok, self.ev())["status"], "pass")
        bad = results([[cell("graceful-restart-crm-api", failed=20, completed=80)]]); self.assertEqual(checks.restart(bad, self.ev())["status"], "fail")

class Verdict(unittest.TestCase):
    CR = {"entry_gate": [{"id": "EG1", "title": "x"}], "rows": [{"id": "R7", "title": "k", "tier": "critical"}, {"id": "R1", "title": "a", "tier": "required"}],
          "independence": [{"id": "IND1", "title": "i", "required": True}]}
    def test_nothing_run_is_not_ready(self):
        self.assertEqual(readiness.verdict(self.CR, tempfile.mkdtemp())["verdict"], "NOT READY")
    def test_accepted_gap_needs_owner(self):
        with self.assertRaises(ValueError): readiness.record(tempfile.mkdtemp(), "R1", "accepted_gap", detail="x")
    def test_critical_failure_blocks_even_when_rest_passes(self):
        d = tempfile.mkdtemp()
        for i in ("EG1", "R1", "IND1"): readiness.record(d, i, "pass")
        readiness.record(d, "R7", "fail")
        self.assertEqual(readiness.verdict(self.CR, d)["verdict"], "NOT READY")
    def test_all_pass_is_production_grade(self):
        d = tempfile.mkdtemp()
        for i in ("EG1", "R1", "R7", "IND1"): readiness.record(d, i, "pass")
        self.assertTrue(readiness.verdict(self.CR, d)["verdict"].startswith("PRODUCTION"))
    def test_real_criteria_file_has_every_id_once(self):
        c = _load(os.path.join(ROOT, "readiness", "criteria.json"))
        ids = [x["id"] for k in ("entry_gate", "rows", "independence") for x in c[k]]; self.assertEqual(len(ids), len(set(ids)))
        self.assertTrue(all(f"R{i}" in ids for i in range(1, 23)))

if __name__ == "__main__": unittest.main()

class Recovery(unittest.TestCase):
    def make(self, slow_until):
        import gzip
        d = tempfile.mkdtemp(); sd = os.path.join(d, "session-01", "journeys"); os.makedirs(sd)
        _dump({"journeys": [{"name": "customer-to-cash-staged", "staged": [{"rate": 1, "s": 20}, {"rate": 5, "s": 20}, {"rate": 1, "s": 100}]}]}, os.path.join(d, "config.json"))
        rows = []
        for i in range(140 * 2):
            t = i * 0.5; rows.append({"intended": t, "end": t + (20.0 if t < slow_until else 0.5), "ok": True})
        with gzip.open(os.path.join(sd, "customer-to-cash-staged-staged-1.journeys.jsonl.gz"), "wt") as f:
            for r in rows: f.write(json.dumps(r) + "\n")
        c = cell("customer-to-cash-staged", offered=len(rows), completed=len(rows), wall_s=150); c["mode"] = "staged"
        _dump(c, os.path.join(sd, "customer-to-cash-staged-staged-1.json")); return d
    def test_recovers_in_time(self):
        self.assertEqual(checks.recovery(self.make(60), tempfile.mkdtemp(), 5, 60)["status"], "pass")
    def test_slow_recovery_fails(self):
        e = checks.recovery(self.make(130), tempfile.mkdtemp(), 5, 20); self.assertEqual(e["status"], "fail")

class Parity(unittest.TestCase):
    def test_decide(self):
        from fpbench import parity as p
        self.assertEqual(p.decide(200, {"data": {}}), "allow"); self.assertEqual(p.decide(200, {"errors": [{}]}), "deny")
        self.assertEqual(p.decide(403, None), "deny"); self.assertEqual(p.decide(503, None), "error")
    def test_placeholders_and_too_few_are_not_run(self):
        from fpbench import parity as p
        d = tempfile.mkdtemp(); cfg = os.path.join(d, "c.json"); _dump({"auth": {"keycloak_url": "http://x"}, "crm_url": "http://x", "erp_url": "http://x"}, cfg)
        v = os.path.join(d, "v.json"); _dump({"vectors": [{"todo": True, "name": "x"}]}, v)
        self.assertEqual(p.run(cfg, v, d)["status"], "not_run")
        _dump({"vectors": [{"name": "a", "user": "u", "surfaces": {}}]}, v); self.assertEqual(p.run(cfg, v, d)["status"], "not_run")

class AuthAttack(unittest.TestCase):
    def serve(self, strict):
        import threading, http.server, base64
        e = lambda x: base64.urlsafe_b64encode(x).decode().rstrip("=")
        good = ".".join([e(b'{"alg":"RS256"}'), e(b'{"sub":"a"}'), "sig1234"])
        class H(http.server.BaseHTTPRequestHandler):
            def log_message(self, *a): pass
            def do_GET(self):
                tok = self.headers.get("Authorization", "")[7:]
                ok = (tok == good) if strict else True
                self.send_response(200 if ok else 401); self.end_headers()
            def do_POST(self):
                self.rfile.read(int(self.headers.get("Content-Length", 0)))
                import json as j; b = j.dumps({"access_token": good}).encode()
                self.send_response(200); self.end_headers(); self.wfile.write(b)
        srv = http.server.ThreadingHTTPServer(("127.0.0.1", 0), H); threading.Thread(target=srv.serve_forever, daemon=True).start(); self.addCleanup(srv.server_close); self.addCleanup(srv.shutdown)
        return srv.server_address[1]
    def run_check(self, strict):
        from fpbench import authattack
        port = self.serve(strict); d = tempfile.mkdtemp(); cfg = os.path.join(d, "c.json")
        _dump({"auth": {"keycloak_url": f"http://127.0.0.1:{port}"}}, cfg)
        return authattack.run(cfg, d, [f"http://127.0.0.1:{port}/x"])
    def test_variants_are_distinct_and_forged(self):
        from fpbench import authattack
        v = authattack.variants("aaa.eyJzdWIiOiJhIn0.cccccccc"); self.assertEqual(len(set(map(str, v.values()))), 5); self.assertTrue(v["alg_none"].endswith("."))
    def test_strict_server_passes(self):
        e = self.run_check(True); self.assertEqual(e["status"], "pass", e); self.assertEqual(e["measured"]["attempts"], 5)
    def test_permissive_server_fails(self):
        e = self.run_check(False); self.assertEqual(e["status"], "fail"); self.assertEqual(e["measured"]["accepted"], 5)
