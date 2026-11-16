from fpbench.io import _wj
import asyncio, json, os, subprocess, sys, tempfile, unittest
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from fpbench import stats, loadgen, sampler, gate, report

def _w(d, name, obj):
    with open(os.path.join(d, name), 'w') as f: json.dump(obj, f)

class Stats(unittest.TestCase):
    def test_nearest_rank(self):
        v = list(range(1, 101))
        self.assertEqual(stats.percentile(v, 50), 50); self.assertEqual(stats.percentile(v, 99), 99)
        self.assertEqual(stats.percentile(v, 100), 100); self.assertTrue(stats.percentile([], 50) != stats.percentile([], 50))
    def test_bootstrap_detects_difference_and_null(self):
        a, b = [100, 104, 98, 101, 103], [10, 10.4, 9.8, 10.1, 10.3]
        ci = stats.bootstrap_ratio_ci(a, b); self.assertTrue(ci["excludes_1"]); self.assertAlmostEqual(ci["ratio"], 10, delta=0.5)
        same = stats.bootstrap_ratio_ci([10, 12, 9, 11, 10], [10, 11, 9, 12, 10]); self.assertFalse(same["excludes_1"])
    def test_bootstrap_is_deterministic(self):
        self.assertEqual(stats.bootstrap_ratio_ci([1, 2, 3], [1, 1, 2]), stats.bootstrap_ratio_ci([1, 2, 3], [1, 1, 2]))
    def test_littles_law(self):
        self.assertTrue(stats.littles_law_check(8, 800, 10.0)["ok"]); self.assertFalse(stats.littles_law_check(8, 800, 30.0)["ok"])

class Canonical(unittest.TestCase):
    def test_key_order_and_ignored_keys(self):
        a = loadgen.canonical(b'{"b":1,"a":[{"id":2,"x":1},{"id":1,"x":2}]}', sort_lists_by="id")
        b = loadgen.canonical(b'{"a":[{"x":2,"id":1},{"x":1,"id":2}],"b":1}', sort_lists_by="id")
        self.assertEqual(a, b)
        self.assertEqual(loadgen.canonical(b'{"id":1,"v":2}', ["id"]), loadgen.canonical(b'{"id":9,"v":2}', ["id"]))
    def test_different_payload_differs(self):
        self.assertNotEqual(loadgen.canonical(b'{"a":1}')[0], loadgen.canonical(b'{"a":2}')[0])

class Sampler(unittest.TestCase):
    def test_parse_bytes(self):
        self.assertEqual(sampler.parse_bytes("1.5GiB / 4GiB"), 1.5 * 1024**3); self.assertEqual(sampler.parse_bytes("512MiB"), 512 * 1024**2)
        self.assertEqual(sampler.parse_bytes("10kB"), 10000); self.assertEqual(sampler.parse_bytes("7B"), 7)
        with self.assertRaises(ValueError): sampler.parse_bytes("lots")
    def test_zero_samples_is_invalid_not_zero(self):
        s = sampler.Sampler({"kind": "pid", "pids": [2**22 + 12345]})
        async def go():
            s.start(); await asyncio.sleep(0.2); await s.stop()
        asyncio.run(go()); out = s.summary()
        self.assertTrue(out["declared"]); self.assertFalse(out["valid"]); self.assertNotIn("cpu_cores_mean", out)
    def test_pid_sampling_works(self):
        s = sampler.Sampler({"kind": "pid", "pids": [os.getpid()]}, interval=0.1)
        async def go():
            s.start(); await asyncio.sleep(0.6); await s.stop()
        asyncio.run(go()); out = s.summary(); self.assertTrue(out["valid"]); self.assertGreater(out["rss_mb_max"], 1)

class Gate(unittest.TestCase):
    def rep(self, lo, eq=True):
        return {"claim_grade": True, "blockers": [], "rows": [{"scenario": "q", "concurrency": 8, "equivalent": eq, "ci": {"lo": lo, "hi": lo * 1.2, "ratio": lo * 1.1}}]}
    def test_pass_and_fail(self):
        self.assertEqual(gate.compare(self.rep(10), self.rep(10)), [])
        self.assertTrue(gate.compare(self.rep(8), self.rep(10)))            # >10% drop
        self.assertTrue(gate.compare(self.rep(0.9), self.rep(2)))           # no longer significant
        self.assertTrue(gate.compare(self.rep(10, eq=False), self.rep(10)))  # equivalence lost

class Report(unittest.TestCase):
    def test_not_claim_grade_with_few_sessions(self):
        d = tempfile.mkdtemp(); os.makedirs(os.path.join(d, "session-01", "s", "t"))
        _w(d, "config.json", {}); _w(d, "environment.json", {"disclosure_complete": True, "missing": []})
        _w(d, "equivalence.json", {"s": {"equivalent": True, "mismatches": []}})
        rep = report.build(d, "a", "b"); self.assertFalse(rep["claim_grade"]); self.assertTrue(any("sessions" in b for b in rep["blockers"]))

if __name__ == "__main__":
    unittest.main()
