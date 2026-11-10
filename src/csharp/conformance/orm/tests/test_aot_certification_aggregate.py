import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from importlib.machinery import SourceFileLoader
AGG = SourceFileLoader('aggregate_aot', str(Path(__file__).resolve().parents[1] / 'aggregate-aot-certification.py')).load_module()

class AggregateCertificationTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def write(self, provider, rid, *, run='123', commit='deadbeef', certified=False, suffix=''):
        folder = self.root / f'{provider}-{rid}{suffix}'
        folder.mkdir(parents=True, exist_ok=True)
        record = {
            'schemaVersion': 2, 'provider': provider, 'runtimeIdentifier': rid,
            'publish': 'success', 'runtimeExecution': 'success' if certified else 'not-attempted',
            'warningBaseline': 'passed', 'certified': certified,
            'classification': ('certified-for-rid' if certified else 'incomplete-or-failed'),
            'provenance': {'workflowRunId': run, 'commitSha': commit, 'runAttempt': '1',
                           'workflowRef': 'repo/.github/workflows/orm-conformance.yml@refs/heads/main'},
        }
        (folder / 'certification-evidence.json').write_text(json.dumps(record))

    def all_lanes(self, x64=False, arm64=False):
        self.write('efcore', 'linux-x64', certified=x64)
        self.write('efcore', 'linux-arm64', certified=arm64)

    def test_complete_efcore_lanes_pass_the_release_gate(self):
        self.all_lanes(x64=True, arm64=True)
        r = AGG.aggregate(self.root, '123', 'deadbeef', '1', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertTrue(r['aggregationValid'])
        self.assertEqual(r['providerSummary']['efcore'], 'certified-all-rids')
        self.assertTrue(r['releaseGate']['passed'])

    def test_arm64_publish_only_prevents_all_rid_certification(self):
        self.all_lanes(x64=True, arm64=False)
        r = AGG.aggregate(self.root, '123', 'deadbeef', '1', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertEqual(r['providerSummary']['efcore'], 'incomplete-or-failed')
        self.assertFalse(r['releaseGate']['passed'])

    def test_missing_lane_is_invalid(self):
        self.write('efcore', 'linux-x64')
        r = AGG.aggregate(self.root, '123', 'deadbeef', '1', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertFalse(r['aggregationValid'])
        self.assertEqual(len(r['missingLanes']), 1)

    def test_stale_workflow_run_is_rejected(self):
        self.all_lanes()
        r = AGG.aggregate(self.root, '999', 'deadbeef', '1', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertFalse(r['aggregationValid'])
        self.assertTrue(r['integrityErrors'])

    def test_different_commit_is_rejected(self):
        self.all_lanes()
        r = AGG.aggregate(self.root, '123', 'different', '1', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertFalse(r['aggregationValid'])

    def test_duplicate_lane_is_rejected(self):
        self.all_lanes()
        self.write('efcore', 'linux-x64', suffix='-duplicate')
        r = AGG.aggregate(self.root, '123', 'deadbeef', '1', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertFalse(r['aggregationValid'])
        self.assertTrue(any('duplicate' in e['error'] for e in r['integrityErrors']))

    def test_unknown_lane_is_rejected(self):
        self.write('efcore', 'win-x64')
        r = AGG.aggregate(self.root, '123', 'deadbeef', '1', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertFalse(r['aggregationValid'])
        self.assertTrue(any('unexpected' in e['error'] for e in r['integrityErrors']))

    def test_wrong_run_attempt_is_rejected(self):
        self.all_lanes()
        r = AGG.aggregate(self.root, '123', 'deadbeef', '2', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertFalse(r['aggregationValid'])

    def test_wrong_workflow_ref_is_rejected(self):
        self.all_lanes()
        r = AGG.aggregate(self.root, '123', 'deadbeef', '1', 'repo/other.yml@refs/heads/main')
        self.assertFalse(r['aggregationValid'])


    def test_release_gate_fails_when_all_lanes_exist_but_efcore_not_certified(self):
        self.all_lanes(x64=False, arm64=False)
        r = AGG.aggregate(self.root, '123', 'deadbeef', '1', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertTrue(r['aggregationValid'])
        self.assertFalse(r['releaseGate']['passed'])

    def test_inconsistent_certified_flag_is_rejected(self):
        self.all_lanes(x64=True, arm64=True)
        path = self.root / 'efcore-linux-x64' / 'certification-evidence.json'
        record = json.loads(path.read_text())
        record['runtimeExecution'] = 'not-attempted'
        path.write_text(json.dumps(record))
        r = AGG.aggregate(self.root, '123', 'deadbeef', '1', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertFalse(r['aggregationValid'])
        self.assertTrue(any('conflicts' in e['error'] for e in r['integrityErrors']))

    def test_report_contains_sha256_and_size_for_each_evidence_file(self):
        self.all_lanes(x64=True, arm64=True)
        r = AGG.aggregate(self.root, '123', 'deadbeef', '1', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertEqual(r['schemaVersion'], 3)
        self.assertEqual(len(r['evidenceFiles']), 2)
        for item in r['evidenceFiles']:
            self.assertEqual(len(item['sha256']), 64)
            self.assertGreater(item['sizeBytes'], 0)
        self.assertEqual(r['lanes'][0]['evidence']['sha256'], r['evidenceFiles'][0]['sha256'])

    def test_non_object_json_is_rejected(self):
        self.all_lanes(x64=True, arm64=True)
        path = self.root / 'efcore-linux-x64' / 'certification-evidence.json'
        path.write_text('[]')
        r = AGG.aggregate(self.root, '123', 'deadbeef', '1', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertFalse(r['aggregationValid'])
        self.assertTrue(any('JSON object' in e['error'] for e in r['integrityErrors']))

    def test_boolean_schema_version_is_rejected(self):
        self.all_lanes(x64=True, arm64=True)
        path = self.root / 'efcore-linux-x64' / 'certification-evidence.json'
        record = json.loads(path.read_text())
        record['schemaVersion'] = True
        path.write_text(json.dumps(record))
        r = AGG.aggregate(self.root, '123', 'deadbeef', '1', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertFalse(r['aggregationValid'])
        self.assertTrue(any('schema' in e['error'] for e in r['integrityErrors']))

    def test_boolean_certified_value_is_not_accepted_as_integer(self):
        self.all_lanes(x64=True, arm64=True)
        path = self.root / 'efcore-linux-x64' / 'certification-evidence.json'
        record = json.loads(path.read_text())
        record['certified'] = 1
        path.write_text(json.dumps(record))
        r = AGG.aggregate(self.root, '123', 'deadbeef', '1', 'repo/.github/workflows/orm-conformance.yml@refs/heads/main')
        self.assertFalse(r['aggregationValid'])
        self.assertTrue(any('boolean' in e['error'] for e in r['integrityErrors']))

if __name__ == '__main__':
    unittest.main()
