import glob, json, os, re, sys, unittest
import yaml
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
sys.path.insert(0, ROOT)
sys.path.insert(0, os.path.join(ROOT, "keycloak"))
import gen_realm

def J(p): return json.load(open(os.path.join(ROOT, p)))

class Wiring(unittest.TestCase):
    def test_every_identity_exists_in_the_realm(self):
        users = {u[0] for u in gen_realm.USERS}
        for f in glob.glob(os.path.join(ROOT, "scenarios", "sc-*.json")) + glob.glob(os.path.join(ROOT, "scenarios", "vectors-supplychain-*.json")):
            d = json.load(open(f))
            for r in d.get("requests", []) + d.get("vectors", []):
                self.assertIn(r.get("as"), users, f"{os.path.basename(f)} uses unknown identity {r.get('as')}")

    def test_realm_matches_crm_erp_claim_shape(self):
        r = gen_realm.build()
        mappers = {m["config"]["claim.name"] for s in r["clientScopes"] for m in s["protocolMappers"] if "claim.name" in m["config"]}
        self.assertTrue({"realm_access.roles", "tenant_id", "customer_id", "preferred_username"} <= mappers)
        self.assertEqual({s["name"] for s in r["clientScopes"]} & {"basic", "profile"}, {"basic", "profile"})   # Keycloak 26 import quirk
        aud = [m for s in r["clientScopes"] for m in s["protocolMappers"] if m["protocolMapper"] == "oidc-audience-mapper"]
        self.assertEqual([m["config"]["included.client.audience"] for m in aud], ["supplychain-api"])
        notenant = [u for u in r["users"] if u["username"] == "notenant"][0]
        self.assertNotIn("attributes", notenant)

    def test_configs_reference_existing_scenarios_and_groups(self):
        for cfgname, group in (("supplychain-csharp", "csharp"), ("supplychain-java", "java")):
            c = J(f"configs/{cfgname}.json")
            for s in c["scenarios"]:
                self.assertTrue(os.path.exists(os.path.normpath(os.path.join(ROOT, "configs", s))), s)
            self.assertEqual({t["group"] for t in c["targets"].values()}, {group})
            self.assertEqual(len(c["targets"]), 2)
            self.assertIn("auth", c)

    def test_foundgine_and_baseline_expect_opposite_migration_tools(self):
        # representative applied-migration ids per runtime: (foundgine tool output, plain tool output)
        examples = {"supplychain-csharp": ("20261117100010_Initial", "20261117101703_Baseline"),
                    "supplychain-java": ("V20261117101500__InitialCreate.sql", "V1__baseline.sql")}
        for cfgname, (fid, bid) in examples.items():
            t = J(f"configs/{cfgname}.json")["targets"]
            f = [v for k, v in t.items() if "foundgine" in k][0]
            b = [v for k, v in t.items() if "baseline" in k][0]
            hit = lambda rxs, x: any(re.search(r, x) for r in rxs)
            self.assertTrue(hit(f["expect_history"], fid) and not hit(f["forbid_history"], fid), cfgname)
            self.assertTrue(hit(b["expect_history"], bid) and not hit(b["forbid_history"], bid), cfgname)
            self.assertTrue(hit(f["forbid_history"], bid) and hit(b["forbid_history"], fid), cfgname)   # each rejects the other's tool
            self.assertIn("schema_cmd", f); self.assertIn("schema_cmd", b)

    def test_each_arm_has_its_own_database(self):
        dbs = []
        for cfgname in ("supplychain-csharp", "supplychain-java"):
            for t in J(f"configs/{cfgname}.json")["targets"].values():
                dbs.append(re.search(r"-d (\w+)", t["schema_cmd"]).group(1))
        self.assertEqual(len(dbs), len(set(dbs)))
        init = open(os.path.join(ROOT, "compose", "supplychain-init.sql")).read()
        for d in dbs: self.assertIn(f"CREATE DATABASE {d};", init)

    def test_compose_parses_and_arms_share_one_audience(self):
        for f in ("keycloak.yml", "supplychain.yml", "supplychain.perf.yml"):
            yaml.safe_load(open(os.path.join(ROOT, "compose", f)))
        c = yaml.safe_load(open(os.path.join(ROOT, "compose", "supplychain.yml")))["services"]
        for svc in ("cs-baseline", "cs-foundgine", "java-baseline", "java-foundgine"):
            env = json.dumps(c[svc]["environment"])
            self.assertIn("supplychain-api", env); self.assertIn("http://keycloak:8080/realms/supplychain", env)

    def test_all_four_arms_have_shared_redis_and_durable_anchor_wiring(self):
        compose = yaml.safe_load(open(os.path.join(ROOT, "compose", "supplychain.yml")))
        services = compose["services"]
        self.assertIn("redis", services)
        self.assertIn("--appendfsync", services["redis"]["command"])
        for name in ("cs-baseline", "cs-foundgine", "java-baseline", "java-foundgine"):
            env = json.dumps(services[name].get("environment", {}))
            self.assertIn("redis", env.lower(), name)
            self.assertIn("redis", json.dumps(services[name].get("depends_on", {})).lower(), name)
        for config in ("supplychain-csharp", "supplychain-java"):
            for target in J(f"configs/{config}.json")["targets"].values():
                self.assertIn("supplychain-redis-1", target["resources"]["containers"])
        hc = open(os.path.join(ROOT, "..", "..", "src", "csharp", "benchmarks", "SupplyChain.Performance", "HotChocolate.SupplyChain.BenchmarkApi", "HotChocolate.SupplyChain.BenchmarkApi.csproj")).read()
        self.assertIn("StackExchange.Redis", hc)
        self.assertIn("Npgsql", hc)
        guard = open(os.path.join(ROOT, "..", "..", "src", "csharp", "benchmarks", "SupplyChain.Performance", "HotChocolate.SupplyChain.BenchmarkApi", "BaselineRevocationEpochGuard.cs")).read()
        self.assertIn("foundgine_security_warrant_revocation_epoch", guard)
        self.assertIn("synchronous_commit", guard)
        fg_cs = open(os.path.join(ROOT, "..", "..", "src", "csharp", "benchmarks", "SupplyChain.Performance", "Foundgine.SupplyChain.BenchmarkApi", "Program.cs")).read()
        self.assertIn("AnchoredSecurityWarrantRevocationStore", fg_cs)
        self.assertIn("RedisSecurityWarrantRevocationStore", fg_cs)
        for arm in ("foundgine", "spring-baseline"):
            java_root = os.path.join(ROOT, "..", "..", "src", "java", "benchmarks", "supply-chain", arm)
            java = os.path.join(java_root, "src", "main", "java", "com", "foundgine", "benchmark")
            filter_name = "BenchmarkRevocationFilter.java" if arm == "foundgine" else "BaselineRevocationFilter.java"
            self.assertTrue(os.path.exists(os.path.join(java, filter_name)))
            self.assertIn("lettuce-core", open(os.path.join(java_root, "pom.xml")).read())

    def test_vectors_cover_every_forged_token_mode_and_both_decisions(self):
        for g in ("csharp", "java"):
            v = J(f"scenarios/vectors-supplychain-{g}.json")["vectors"]
            self.assertEqual({x["token_mode"] for x in v} - {"valid"}, {"none", "garbage", "bad_signature", "alg_none", "edited_payload"})
            self.assertTrue({"allow", "deny", "unauthenticated"} <= {x["expect"]["decision"] for x in v})

if __name__ == "__main__":
    unittest.main()
