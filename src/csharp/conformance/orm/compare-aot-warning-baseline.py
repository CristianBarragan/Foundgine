#!/usr/bin/env python3
"""Compare AOT warning fingerprints against a manually reviewed baseline."""
import argparse
import json
from pathlib import Path

BLOCKING = {"IL2026", "IL3050"}
OWNERS = {"foundgine", "provider-or-dependency", "toolchain-or-unclassified"}


def load_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    report = load_json(args.report)
    baseline = load_json(args.baseline)
    if report.get("schemaVersion") != 4:
        raise SystemExit("Unsupported diagnostics report schema; expected 4")
    if baseline.get("schemaVersion") != 1 or not isinstance(baseline.get("warnings"), list):
        raise SystemExit("Invalid warning baseline schema")
    approved = {}
    for entry in baseline["warnings"]:
        if not isinstance(entry, dict) or not all(entry.get(k) for k in ("fingerprint", "code", "owner", "rationale", "reviewedBy")):
            raise SystemExit("Every baseline entry requires fingerprint, code, owner, rationale, and reviewedBy")
        if entry["owner"] not in OWNERS or entry["code"] not in BLOCKING:
            raise SystemExit("Baseline may contain only explicitly reviewed IL2026/IL3050 warnings")
        if entry["fingerprint"] in approved:
            raise SystemExit("Duplicate fingerprint in warning baseline")
        approved[entry["fingerprint"]] = entry
    current = {w["fingerprint"]: w for w in report.get("warnings", [])}
    new = [w for fp, w in current.items() if fp not in approved]
    existing = [w for fp, w in current.items() if fp in approved]
    resolved = [e for fp, e in approved.items() if fp not in current]
    new_blocking = [w for w in new if w["owner"] == "foundgine" and w["code"] in BLOCKING]
    payload = {
        "schemaVersion": 1,
        "baselineEntryCount": len(approved),
        "currentWarningCount": len(current),
        "newWarnings": new,
        "existingWarnings": existing,
        "resolvedWarnings": resolved,
        "newFoundgineBlockingWarnings": new_blocking,
        "enforcement": {"enabled": True, "blockingCount": len(new_blocking), "result": "failed" if new_blocking else "passed"},
        "baselineWasModified": False,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(f"AOT baseline: new={len(new)}, existing={len(existing)}, resolved={len(resolved)}, new blocking={len(new_blocking)}")
    return 2 if new_blocking else 0


if __name__ == "__main__":
    raise SystemExit(main())
