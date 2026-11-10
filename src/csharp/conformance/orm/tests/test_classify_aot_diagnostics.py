import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "classify-aot-diagnostics.py"


class ClassifierTests(unittest.TestCase):
    def run_classifier(self, log_text, *flags):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            log = root / "publish.log"
            report = root / "report.json"
            log.write_text(log_text, encoding="utf-8")
            result = subprocess.run(
                [sys.executable, str(SCRIPT), str(log), str(report), *flags],
                text=True, capture_output=True, check=False,
            )
            payload = json.loads(report.read_text(encoding="utf-8"))
            return result, payload

    def test_foundgine_source_warning_is_blocking_when_gate_enabled(self):
        result, report = self.run_classifier(
            "src/csharp/Foundgine.Providers.Orm/Runtime.cs(12,4): warning IL2026: Calls a method requiring unreferenced code. [Foundgine.Providers.Orm.csproj]",
            "--fail-on-foundgine",
        )
        self.assertEqual(result.returncode, 2)
        self.assertEqual(report["blockingFoundgineWarnings"][0]["code"], "IL2026")

    def test_dependency_path_is_not_foundgine_owned(self):
        result, report = self.run_classifier(
            "C:/agent/.nuget/packages/microsoft.entityframeworkcore/10.0.0/lib/net10.0/EF.cs(1,1): warning IL3050: Dynamic code warning"
        )
        self.assertEqual(result.returncode, 0)
        self.assertEqual(report["warnings"][0]["owner"], "provider-or-dependency")

    def test_incidental_foundgine_word_in_message_is_not_owned(self):
        result, report = self.run_classifier(
            "warning IL2026: This third-party method is used by Foundgine. [External.csproj]"
        )
        self.assertEqual(result.returncode, 0)
        self.assertEqual(report["warnings"][0]["owner"], "toolchain-or-unclassified")

    def test_foundgine_il3050_is_blocking_too(self):
        result, report = self.run_classifier(
            "src/csharp/Foundgine.Core/Runtime.cs(8,2): warning IL3050: Dynamic code may be required",
            "--fail-on-foundgine",
        )
        self.assertEqual(result.returncode, 2)
        self.assertEqual(report["blockingFoundgineWarnings"][0]["code"], "IL3050")

    def test_other_foundgine_il_warning_is_reported_but_not_blocking(self):
        result, report = self.run_classifier(
            "src/csharp/Foundgine.Core/Runtime.cs(3,1): warning IL2090: Other analyzer warning",
            "--fail-on-foundgine",
        )
        self.assertEqual(result.returncode, 0)
        self.assertEqual(report["countsByOwner"]["foundgine"], 1)
        self.assertEqual(report["blockingFoundgineWarnings"], [])

    def test_fingerprint_is_stable_when_line_and_column_move(self):
        _, first = self.run_classifier(
            "src/csharp/Foundgine.Core/Runtime.cs(3,1): warning IL2026: Calls a method requiring unreferenced code"
        )
        _, moved = self.run_classifier(
            "src/csharp/Foundgine.Core/Runtime.cs(99,27): warning IL2026: Calls a method requiring unreferenced code"
        )
        self.assertEqual(first["warnings"][0]["fingerprint"], moved["warnings"][0]["fingerprint"])

    def test_generated_source_is_attributed_by_foundgine_project(self):
        _, report = self.run_classifier(
            "src/csharp/Foundgine.Core/obj/Release/net10.0/generated/Foundgine.Generator/Mapper.g.cs(7,3): warning IL2026: Calls a method requiring unreferenced code. [src/csharp/Foundgine.Core/Foundgine.Core.csproj]"
        )
        warning = report["warnings"][0]
        self.assertEqual(warning["owner"], "foundgine")
        self.assertEqual(warning["sourceKind"], "compiler-generated")
        self.assertTrue(warning["generatorPath"].endswith("Mapper.g.cs"))
        self.assertEqual(warning["ownershipEvidence"], "consuming-project")

    def test_project_only_diagnostic_uses_project_identity(self):
        _, report = self.run_classifier(
            "warning IL3050: Dynamic code may be required. [D:\\agent\\_work\\Foundgine.Core.csproj]"
        )
        self.assertEqual(report["warnings"][0]["owner"], "foundgine")
        self.assertEqual(report["warnings"][0]["sourceKind"], "project-only")

    def test_package_source_takes_precedence_over_foundgine_project(self):
        _, report = self.run_classifier(
            "C:/agent/.nuget/packages/example-orm/1.0.0/lib/net10.0/Proxy.g.cs(1,1): warning IL2026: Dynamic code [src/Foundgine.Core.csproj]"
        )
        self.assertEqual(report["warnings"][0]["owner"], "provider-or-dependency")

    def test_generated_fingerprint_ignores_agent_checkout_root_and_line(self):
        _, first = self.run_classifier(
            "C:/agent/_work/repo/src/csharp/Foundgine.Core/obj/Release/generated/Mapper.g.cs(3,1): warning IL2026: Calls method [Foundgine.Core.csproj]"
        )
        _, moved = self.run_classifier(
            "D:/runner/work/repo/src/csharp/Foundgine.Core/obj/Release/generated/Mapper.g.cs(99,27): warning IL2026: Calls method [Foundgine.Core.csproj]"
        )
        self.assertEqual(first["warnings"][0]["fingerprint"], moved["warnings"][0]["fingerprint"])


if __name__ == "__main__":
    unittest.main()
