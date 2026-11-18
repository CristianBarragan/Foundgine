"""Static checks for tools/foundgine-perf/java (counterpart of csharp/). No Maven or database is used."""
import pathlib
import subprocess
import unittest

PERF = pathlib.Path(__file__).resolve().parents[1]
JAVA = PERF / "java"


class JavaMigrationScripts(unittest.TestCase):
    def test_scripts_exist_and_are_valid_bash(self):
        for name in ("generate-foundgine-migration.sh", "generate-baseline-migration.sh", "verify-migration-provenance.sh"):
            path = JAVA / name
            self.assertTrue(path.is_file(), name)
            subprocess.run(["bash", "-n", str(path)], check=True)

    def test_provenance_script_passes_on_checked_in_sources(self):
        result = subprocess.run(["bash", str(JAVA / "verify-migration-provenance.sh")], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("SOURCE_PROVENANCE_OK", result.stdout)

    def test_foundgine_script_uses_foundgine_migrations_cli(self):
        script = (JAVA / "generate-foundgine-migration.sh").read_text()
        self.assertIn("migrations.sh", script)
        self.assertIn("migrations add InitialCreate", script)


if __name__ == "__main__":
    unittest.main()
