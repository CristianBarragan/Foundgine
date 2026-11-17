import copy, json, os, sys, tempfile, unittest
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from fpbench import schemaparity as sp

BASE = {"columns": [["suppliers", "supplier_id", "integer", "NO"], ["suppliers", "tenant_id", "character varying", "NO"]],
        "keys": [["suppliers", "pk", ["supplier_id"]]], "fks": [], "indexes": [["suppliers", ["tenant_id"]]],
        "history": {"kind": "flyway", "applied": ["V20261114161434__InitialCreate.sql"]}}

class Parity(unittest.TestCase):
    def test_same_schema_different_history_is_equal(self):
        b = copy.deepcopy(BASE); b["history"] = {"kind": "flyway", "applied": ["V1__baseline.sql"]}
        self.assertEqual(sp.diff(BASE, b), [])

    def test_column_type_difference_is_reported(self):
        b = copy.deepcopy(BASE); b["columns"][0][2] = "bigint"
        self.assertTrue(any("columns" in d for d in sp.diff(BASE, b)))

    def test_missing_index_is_reported(self):
        b = copy.deepcopy(BASE); b["indexes"] = []
        self.assertTrue(any("indexes" in d for d in sp.diff(BASE, b)))

    def test_no_history_fails_provenance(self):
        d = copy.deepcopy(BASE); d["history"] = {"kind": "none", "applied": []}
        self.assertTrue(sp.provenance(d, {"history_kind": "flyway"}))

    def test_foundgine_arm_needs_foundgine_migration_and_baseline_must_not_have_it(self):
        self.assertEqual(sp.provenance(BASE, {"history_kind": "flyway", "expect_history": [r"__InitialCreate\.sql$"]}), [])
        self.assertTrue(sp.provenance(BASE, {"history_kind": "flyway", "forbid_history": [r"__InitialCreate\.sql$"]}))
        self.assertTrue(sp.provenance(BASE, {"history_kind": "ef"}))

    def test_run_end_to_end_with_stub_runner(self):
        docs = {"f": BASE, "b": dict(copy.deepcopy(BASE), history={"kind": "flyway", "applied": ["V1__baseline.sql"]})}
        class R:  # stands in for subprocess.run
            def __init__(s, out): s.stdout, s.stderr, s.returncode = out, "", 0
        def runner(cmd, **kw): return R(json.dumps(docs[cmd.split()[-1]]))
        cfg = {"targets": {"f": {"schema_cmd": "x f", "group": "java", "history_kind": "flyway", "expect_history": ["InitialCreate"]},
                           "b": {"schema_cmd": "x b", "group": "java", "history_kind": "flyway", "expect_history": ["baseline"], "forbid_history": ["InitialCreate"]}}}
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "c.json"); json.dump(cfg, open(p, "w"))
            rep = sp.run(p, d, runner)
            self.assertTrue(rep["pass"], rep["failures"])
            docs["b"]["columns"].append(["suppliers", "extra", "text", "YES"])
            self.assertFalse(sp.run(p, d, runner)["pass"])

if __name__ == "__main__":
    unittest.main()
