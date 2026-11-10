#!/usr/bin/env python3
"""Write an honest, provider/RID-scoped Native AOT evidence record."""
import argparse
import json
from pathlib import Path


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--provider', choices=('efcore',), required=True)
    p.add_argument('--rid', required=True)
    p.add_argument('--publish', choices=('success', 'failure', 'skipped'), required=True)
    p.add_argument('--runtime', choices=('success', 'failure', 'skipped', 'not-attempted'), required=True)
    p.add_argument('--baseline', choices=('passed', 'failed', 'missing', 'not-applicable'), default='not-applicable')
    p.add_argument('--output', required=True)
    p.add_argument('--commit-sha', default='unknown')
    p.add_argument('--run-id', default='unknown')
    p.add_argument('--run-attempt', default='unknown')
    p.add_argument('--workflow-ref', default='unknown')
    a = p.parse_args()
    certified = a.publish == 'success' and a.runtime == 'success' and a.baseline == 'passed'
    classification = 'certified-for-rid' if certified else 'incomplete-or-failed'
    record = {
        'schemaVersion': 2, 'provider': a.provider, 'runtimeIdentifier': a.rid,
        'provenance': {'commitSha': a.commit_sha, 'workflowRunId': a.run_id,
                       'runAttempt': a.run_attempt, 'workflowRef': a.workflow_ref},
        'publish': a.publish, 'runtimeExecution': a.runtime,
        'warningBaseline': a.baseline, 'certified': certified,
        'classification': classification,
        'certificationRule': 'EF Core requires successful Native AOT publish, native execution, and warning-baseline comparison.'
    }
    out = Path(a.output)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
    print(f"{a.provider}/{a.rid}: {classification}")

if __name__ == '__main__':
    main()
