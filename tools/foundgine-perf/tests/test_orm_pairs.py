"""Static consistency checks for the ORM-pair arms (no .NET/Java/Docker needed).
They catch wiring drift (ports, service names, linked files, entrypoints); they do NOT prove the arms build or run."""
import json, os, re, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))          # repo root
PERF = os.path.join(ROOT, "tools", "foundgine-perf")
CB = os.path.join(ROOT, "src", "csharp", "benchmarks", "CoffeeBeanery.Performance")
EF = os.path.join(CB, "Foundgine.EfCore.CoffeeBeanery.BenchmarkApi")


class CSharpEfArm(unittest.TestCase):
    def test_linked_shared_files_exist(self):
        csproj = open(os.path.join(EF, "Foundgine.EfCore.CoffeeBeanery.BenchmarkApi.csproj")).read()
        links = re.findall(r'Compile Include="([^"]+)"', csproj)
        self.assertEqual(len(links), 3)
        for rel in links:
            self.assertTrue(os.path.exists(os.path.normpath(os.path.join(EF, rel))), rel)

    def test_project_references_exist(self):
        csproj = open(os.path.join(EF, "Foundgine.EfCore.CoffeeBeanery.BenchmarkApi.csproj")).read()
        for rel in re.findall(r'ProjectReference Include="([^"]+)"', csproj):
            self.assertTrue(os.path.exists(os.path.normpath(os.path.join(EF, rel))), rel)

    def test_ef_versions_match_hot_chocolate_arm(self):
        hc = open(os.path.join(CB, "HotChocolate.CoffeeBeanery.BenchmarkApi",
                               "HotChocolate.CoffeeBeanery.BenchmarkApi.csproj")).read()
        ef = open(os.path.join(EF, "Foundgine.EfCore.CoffeeBeanery.BenchmarkApi.csproj")).read()
        for pkg in ("Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Relational",
                    "Npgsql.EntityFrameworkCore.PostgreSQL"):
            pat = r'Include="%s" Version="([^"]+)"' % re.escape(pkg)
            self.assertEqual(re.search(pat, hc).group(1), re.search(pat, ef).group(1), pkg)

    def test_dockerfile_entrypoint_and_csproj_path(self):
        df = open(os.path.join(EF, "Dockerfile")).read()
        self.assertIn("Foundgine.EfCore.CoffeeBeanery.BenchmarkApi.dll", df)
        self.assertNotIn("Foundgine.CoffeeBeanery.BenchmarkApi/Foundgine.CoffeeBeanery", df)

    def test_compose_port_override_and_config_agree(self):
        compose = open(os.path.join(CB, "compose", "query.yml")).read()
        override = open(os.path.join(PERF, "compose", "perf.override.yml")).read()
        cfg = json.load(open(os.path.join(PERF, "configs", "coffeebeanery.json")))
        self.assertIn("ASPNETCORE_URLS: http://+:4303", compose)
        self.assertIn('"4303:4303"', override)
        t = cfg["targets"]["foundgine-ef"]
        self.assertTrue(t["url"].startswith("http://127.0.0.1:4303/graphql/"))
        self.assertIn("foundgine-ef", t["reset_cmd"])
        self.assertEqual(t["resources"]["containers"], ["coffeebeanery-query-foundgine-ef-1"])

    def test_every_target_has_equal_limits(self):
        override = open(os.path.join(PERF, "compose", "perf.override.yml")).read()
        lim = re.findall(r'(hotchocolate|foundgine-\w+):\s*\{[^}]*mem_limit: (\w+), cpus: (\d+)', override)
        self.assertGreaterEqual(len(lim), 4)
        self.assertEqual({(m, c) for _, m, c in lim}, {("1g", "2")})


if __name__ == "__main__":
    unittest.main()
