"""Static contract tests for the GitHub Native AOT report attestation job."""
from pathlib import Path
import unittest

WORKFLOW = Path(__file__).resolve().parents[5] / '.github' / 'workflows' / 'orm-conformance.yml'


class AotAttestationWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = WORKFLOW.read_text(encoding='utf-8')
        marker = '  aggregate-aot-certification:\n'
        if marker not in cls.source:
            raise AssertionError('aggregate-aot-certification job is missing')
        cls.job_source = cls.source.split(marker, 1)[1]

    def test_attestation_permissions_are_scoped_to_aggregate_job(self):
        self.assertIn('      contents: read', self.job_source)
        self.assertIn('      id-token: write', self.job_source)
        self.assertIn('      attestations: write', self.job_source)
        self.assertNotIn('id-token: write', self.source.split('jobs:', 1)[0])

    def test_attestation_subject_is_exact_aggregate_report(self):
        self.assertIn('id: attest', self.job_source)
        self.assertIn('uses: actions/attest-build-provenance@v2', self.job_source)
        self.assertIn('subject-path: artifacts/aot-certification/aggregate-report.json', self.job_source)
        self.assertIn("github.event_name != 'pull_request'", self.job_source)

    def test_release_gate_requires_attestation_for_non_pr_runs(self):
        gate = self.job_source.split('name: Enforce Native AOT release certification gate', 1)[1]
        self.assertIn('steps.attest.outcome', gate)
        self.assertIn('GITHUB_EVENT_NAME', gate)
        self.assertIn('attestation is missing or failed', gate)

    def test_attestation_bundle_is_uploaded_as_separate_artifact(self):
        upload = self.job_source.split('name: Upload attestation bundle', 1)[1]
        self.assertIn('uses: actions/upload-artifact@v4', upload)
        self.assertIn('name: orm-aot-certification-attestation', upload)
        self.assertIn('steps.attest.outputs.bundle-path', upload)


if __name__ == '__main__':
    unittest.main()
