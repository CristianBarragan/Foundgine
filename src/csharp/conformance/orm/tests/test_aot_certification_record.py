import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / 'record-aot-certification.py'

class CertificationRecordTests(unittest.TestCase):
    def record(self, provider='efcore', rid='linux-x64', publish='success', runtime='success', baseline='passed'):
        with tempfile.TemporaryDirectory() as d:
            out = Path(d) / 'record.json'
            cmd = [sys.executable, str(SCRIPT), '--provider', provider, '--rid', rid,
                   '--publish', publish, '--runtime', runtime, '--baseline', baseline,
                   '--commit-sha', 'abc123', '--run-id', '98765', '--run-attempt', '2',
                   '--workflow-ref', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main',
                   '--output', str(out)]
            result = subprocess.run(cmd, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            return json.loads(out.read_text())

    def test_efcore_certified_only_with_publish_runtime_and_baseline(self):
        self.assertTrue(self.record()['certified'])

    def test_efcore_publish_without_runtime_is_not_certified(self):
        r = self.record(runtime='not-attempted')
        self.assertFalse(r['certified'])
        self.assertEqual(r['classification'], 'incomplete-or-failed')

    def test_efcore_failed_baseline_is_not_certified(self):
        self.assertFalse(self.record(baseline='failed')['certified'])

    def test_efcore_failed_publish_is_not_certified(self):
        self.assertFalse(self.record(publish='failure')['certified'])

    def test_record_is_rid_scoped(self):
        self.assertEqual(self.record(rid='linux-arm64')['runtimeIdentifier'], 'linux-arm64')

    def test_record_carries_workflow_provenance(self):
        r = self.record()
        self.assertEqual(r['schemaVersion'], 2)
        self.assertEqual(r['provenance']['commitSha'], 'abc123')
        self.assertEqual(r['provenance']['workflowRunId'], '98765')
        self.assertEqual(r['provenance']['runAttempt'], '2')

if __name__ == '__main__':
    unittest.main()
