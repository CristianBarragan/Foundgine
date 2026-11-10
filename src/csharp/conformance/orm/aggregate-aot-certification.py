#!/usr/bin/env python3
"""Aggregate provider/RID AOT evidence with provenance and content integrity."""
import argparse
import hashlib
import json
from pathlib import Path

EXPECTED = {
    ('efcore', 'linux-x64'),
    ('efcore', 'linux-arm64'),
}


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def aggregate(root: Path, expected_run_id: str, expected_commit: str,
              expected_run_attempt: str, expected_workflow_ref: str):
    root = root.resolve()
    records = []
    errors = []
    evidence_files = []
    for path in sorted(root.rglob('certification-evidence.json')):
        try:
            if path.is_symlink():
                raise ValueError('evidence file must not be a symbolic link')
            raw = path.read_bytes()
            record = json.loads(raw.decode('utf-8'))
            if not isinstance(record, dict):
                raise ValueError('evidence document must be a JSON object')
            if type(record.get('schemaVersion')) is not int or record['schemaVersion'] != 2:
                raise ValueError('unsupported evidence schema')
            provenance = record.get('provenance')
            if not isinstance(provenance, dict):
                raise ValueError('provenance must be a JSON object')
            expected_provenance = {
                'workflowRunId': expected_run_id,
                'commitSha': expected_commit,
                'runAttempt': expected_run_attempt,
                'workflowRef': expected_workflow_ref,
            }
            for field, expected in expected_provenance.items():
                if not isinstance(provenance.get(field), str) or provenance[field] != expected:
                    raise ValueError(f'{field} does not match aggregation provenance')
            provider, rid = record.get('provider'), record.get('runtimeIdentifier')
            if not isinstance(provider, str) or not isinstance(rid, str):
                raise ValueError('provider and runtimeIdentifier must be strings')
            key = (provider, rid)
            if key not in EXPECTED:
                raise ValueError(f'unexpected provider/RID lane: {key}')
            publish = record.get('publish')
            runtime = record.get('runtimeExecution')
            baseline = record.get('warningBaseline')
            certified = record.get('certified')
            if publish not in {'success', 'failure', 'skipped'}:
                raise ValueError('invalid publish status')
            if runtime not in {'success', 'failure', 'skipped', 'not-attempted'}:
                raise ValueError('invalid runtime execution status')
            if baseline not in {'passed', 'failed', 'missing', 'not-applicable'}:
                raise ValueError('invalid warning baseline status')
            if type(certified) is not bool:
                raise ValueError('certified must be a boolean')
            expected_certified = (provider == 'efcore' and publish == 'success'
                                  and runtime == 'success' and baseline == 'passed')
            if certified is not expected_certified:
                raise ValueError('certified flag conflicts with required evidence statuses')
            relative_path = path.relative_to(root).as_posix()
            evidence = {'path': relative_path, 'sizeBytes': len(raw),
                        'sha256': hashlib.sha256(raw).hexdigest()}
            evidence_files.append(evidence)
            records.append((key, record, evidence))
        except Exception as exc:
            try:
                display_path = path.relative_to(root).as_posix()
            except ValueError:
                display_path = str(path)
            errors.append({'path': display_path, 'error': str(exc)})

    by_key = {}
    for key, record, evidence in records:
        if key in by_key:
            errors.append({'path': evidence['path'], 'error': f'duplicate evidence for {key}'})
        else:
            by_key[key] = (record, evidence)
    missing = sorted([{'provider': p, 'runtimeIdentifier': rid}
                      for p, rid in EXPECTED if (p, rid) not in by_key],
                     key=lambda x: (x['provider'], x['runtimeIdentifier']))

    lanes = []
    for key in sorted(EXPECTED):
        pair = by_key.get(key)
        if pair is None:
            continue
        record, evidence = pair
        lanes.append({
            'provider': key[0], 'runtimeIdentifier': key[1],
            'publish': record.get('publish'),
            'runtimeExecution': record.get('runtimeExecution'),
            'warningBaseline': record.get('warningBaseline'),
            'certified': record.get('certified') is True,
            'classification': record.get('classification'),
            'provenance': record.get('provenance'),
            'evidence': evidence,
        })

    efcore_lanes = [x for x in lanes if x['provider'] == 'efcore']
    efcore_complete = len(efcore_lanes) == 2 and all(x['certified'] for x in efcore_lanes)
    release_gate_passed = (not errors and not missing and efcore_complete)
    report = {
        'schemaVersion': 3,
        'aggregation': {'workflowRunId': expected_run_id, 'commitSha': expected_commit,
                        'runAttempt': expected_run_attempt, 'workflowRef': expected_workflow_ref},
        'expectedLanes': [{'provider': p, 'runtimeIdentifier': rid}
                          for p, rid in sorted(EXPECTED)],
        'presentLaneCount': len(lanes), 'missingLanes': missing,
        'lanes': lanes,
        'evidenceFiles': sorted(evidence_files, key=lambda item: item['path']),
        'providerSummary': {
            'efcore': 'certified-all-rids' if efcore_complete else 'incomplete-or-failed',
        },
        'integrityErrors': errors,
        'aggregationValid': not errors and not missing,
        'overallCertification': 'not-certified' if not efcore_complete else 'efcore-certified-all-rids',
        'releaseGate': {'required': True, 'passed': release_gate_passed,
                        'reason': 'All EF Core runtime lanes certified and evidence content integrity validated.'
                        if release_gate_passed else 'Requires complete, integrity-valid evidence and successful EF Core certification on every required RID.'},
    }
    return report


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--input-root', required=True)
    parser.add_argument('--expected-run-id', required=True)
    parser.add_argument('--expected-commit', required=True)
    parser.add_argument('--expected-run-attempt', required=True)
    parser.add_argument('--expected-workflow-ref', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    report = aggregate(Path(args.input_root), args.expected_run_id, args.expected_commit,
                       args.expected_run_attempt, args.expected_workflow_ref)
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2, sort_keys=True) + '\n', encoding='utf-8')
    print(f"AOT evidence lanes: {report['presentLaneCount']}/{len(EXPECTED)}; "
          f"valid={report['aggregationValid']}; {report['overallCertification']}")
    if not report['aggregationValid'] or not report['releaseGate']['passed']:
        raise SystemExit(2)


if __name__ == '__main__':
    main()
