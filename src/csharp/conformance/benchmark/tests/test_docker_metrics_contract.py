from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[3]
SCRIPTS = ROOT / "benchmarks" / "AgentEndToEnd" / "scripts"


class DockerMetricsContractTests(unittest.TestCase):
    def test_collector_persists_diagnostics_and_fails_on_zero_samples(self):
        source = (SCRIPTS / "docker-metrics.ps1").read_text(encoding="utf-8-sig")
        self.assertIn("Write-CollectorError", source)
        self.assertIn("docker compose ps failed", source)
        self.assertIn("docker stats failed", source)
        self.assertIn("Status = $status", source)
        self.assertIn("if ($rows -eq 0) { exit 2 }", source)
        self.assertIn("if ($script:ErrorCount -gt 0) { exit 3 }", source)
        self.assertNotIn("$ErrorActionPreference = 'SilentlyContinue'", source)

    def test_summary_never_reports_empty_metrics_as_success(self):
        source = (SCRIPTS / "summarize-docker-metrics.ps1").read_text(encoding="utf-8-sig")
        self.assertIn("Status = 'no-samples'", source)
        self.assertIn("Write-Error", source)
        self.assertIn("exit 2", source)
        self.assertIn("Status = $summaryStatus", source)
        self.assertIn("if ($errors.Count -gt 0)", source)
        self.assertIn("ErrorCount = $errors.Count", source)

    def test_run1_to_run4_propagate_metrics_failure(self):
        for run in (1, 2, 3):
            source = (ROOT / "benchmarks" / "AgentEndToEnd" / f"Run{run}" / "run-agent-end-to-end.ps1").read_text(encoding="utf-8-sig")
            self.assertIn("-ErrorLog", source)
            self.assertIn("Docker metrics summary failed", source)
        source = (ROOT / "benchmarks" / "AgentEndToEnd" / "Run4" / "run-run4.ps1").read_text(encoding="utf-8-sig")
        self.assertIn("-ErrorLog", source)
        self.assertIn("Docker metrics summary failed", source)

    def test_outbox_skip_locked_exists_in_both_runtimes(self):
        csharp = (ROOT / "Foundgine.Providers" / "Outbox" / "PostgresOutbox.cs").read_text()
        java = (Path(__file__).resolve().parents[4] / "java" / "foundgine-providers" / "src" / "main" / "java" / "com" / "foundgine" / "providers" / "outbox" / "PostgresOutbox.java").read_text()
        self.assertIn("FOR UPDATE SKIP LOCKED", csharp)
        self.assertIn("FOR UPDATE SKIP LOCKED", java)


if __name__ == "__main__":
    unittest.main()
