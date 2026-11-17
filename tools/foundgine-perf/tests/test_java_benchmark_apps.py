"""Static contract checks for the Java SupplyChain application scaffolds.

These tests intentionally do not claim compilation or live PostgreSQL coverage.
"""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[3]
BASE = ROOT / "src/java/benchmarks/supply-chain/spring-baseline"
FG = ROOT / "src/java/benchmarks/supply-chain/foundgine"


class JavaBenchmarkApps(unittest.TestCase):
    def test_both_arms_are_maven_reactor_modules(self):
        pom = (ROOT / "src/java/pom.xml").read_text()
        self.assertIn("<module>benchmarks/supply-chain/spring-baseline</module>", pom)
        self.assertIn("<module>benchmarks/supply-chain/foundgine</module>", pom)
        self.assertTrue((BASE / "pom.xml").is_file())
        self.assertTrue((FG / "pom.xml").is_file())

    def test_baseline_owns_flyway_migration_and_hibernate_validates(self):
        migration = BASE / "src/main/resources/db/migration/V1__baseline.sql"
        config = (BASE / "src/main/resources/application.yml").read_text()
        pom = (BASE / "pom.xml").read_text()
        self.assertTrue(migration.is_file())
        self.assertIn("ddl-auto: validate", config)
        self.assertIn("flyway-core", pom)
        self.assertIn("spring-boot-starter-data-jpa", pom)

    def test_foundgine_arm_uses_real_foundgine_jpa_reader_and_gateway(self):
        source = (FG / "src/main/java/com/foundgine/benchmark/SupplyChainGraphQl.java").read_text()
        self.assertIn("db.orm().reader(em)", source)
        self.assertIn("db.entities(", source)
        self.assertIn("gateway.update(id", source)
        self.assertIn("SUPPLYCHAIN_JDBC_URL", (ROOT / "tools/foundgine-perf/compose/supplychain.yml").read_text())

    def test_both_arms_expose_scenario_operations_and_jwt_auth(self):
        for project in (BASE, FG):
            schema = (project / "src/main/resources/graphql/schema.graphqls").read_text()
            auth = (project / "src/main/java/com/foundgine/benchmark/Caller.java").read_text()
            config = (project / "src/main/resources/application.yml").read_text()
            for operation in ("product(first", "supplier(first", "warehouse(first", "updateWarehouse("):
                self.assertIn(operation, schema)
            self.assertIn('"tenant_id"', auth)
            self.assertIn('"realm_access"', auth)
            self.assertIn("issuer-uri:", config)
            self.assertIn("audiences:", config)

    def test_foundgine_migration_is_not_duplicated_in_app_classpath(self):
        self.assertFalse((FG / "src/main/resources/db/migration/V20261114161434__InitialCreate.sql").exists())
        provenance = (FG / "MIGRATION-PROVENANCE.md").read_text()
        self.assertIn("foundgine-sample-supply-chain", provenance)
        self.assertTrue((ROOT / "src/java/samples/foundgine-supply-chain/src/main/resources/db/migration/V20261114161434__InitialCreate.sql").is_file())


if __name__ == "__main__":
    unittest.main()
