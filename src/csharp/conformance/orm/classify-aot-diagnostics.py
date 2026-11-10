#!/usr/bin/env python3
"""Classify Native AOT diagnostics using source, generated-file and project evidence."""
import argparse
import hashlib
import json
import re
from pathlib import Path

WARNING = re.compile(r"(?P<code>IL\d{4}|[A-Z]{2,}\d{4}):\s*(?P<message>.*)")
LOCATION = re.compile(r"^(?P<location>.*?):\s*warning\s+(?:IL\d{4}|[A-Z]{2,}\d{4}):", re.IGNORECASE)
PROJECT = re.compile(r"\[(?P<project>[^\]]+\.csproj)\]", re.IGNORECASE)
DEPENDENCY_MARKERS = (
    "/.nuget/packages/", "/packages/microsoft.entityframeworkcore",
    "/packages/microsoft.data.sqlite",
    "/packages/sqlitepclraw",
)


def diagnostic_parts(line: str) -> dict:
    location_match = LOCATION.search(line)
    project_match = PROJECT.search(line)
    location = location_match.group("location").strip() if location_match else ""
    project = project_match.group("project").strip() if project_match else ""
    source = re.sub(r"\(\d+(?:,\d+)?\)$", "", location).replace("\\", "/")
    normalized_project = project.replace("\\", "/")
    generated = bool(re.search(r"(?:^|/)obj/(?:[^/]+/)*(?:generated/|[^/]+\.g\.cs$)", source, re.I)
                     or re.search(r"\.g\.cs$", source, re.I))
    if generated:
        source_kind = "compiler-generated"
    elif source:
        source_kind = "source"
    else:
        source_kind = "project-only" if project else "unknown"
    return {"sourcePath": source or None, "project": project or None,
            "sourceKind": source_kind, "generated": generated,
            "generatorPath": source if generated else None}


def classify_owner(line: str) -> tuple[str, str]:
    parts = diagnostic_parts(line)
    source = (parts["sourcePath"] or "").lower()
    project = (parts["project"] or "").replace("\\", "/").lower()
    # A diagnostic physically emitted from a package remains dependency-owned,
    # even when the consuming project is Foundgine.
    if any(marker in source for marker in DEPENDENCY_MARKERS):
        return "provider-or-dependency", "source-path-package"
    project_name = project.rsplit("/", 1)[-1]
    if project_name.startswith("foundgine.") and project_name.endswith(".csproj"):
        return "foundgine", "consuming-project"
    if "/foundgine." in source or source.startswith("foundgine."):
        return "foundgine", "source-path"
    return "toolchain-or-unclassified", "insufficient-source-or-project-evidence"


def stable_path(path: str | None) -> str:
    if not path:
        return ""
    value = path.replace("\\", "/").lower()
    value = re.sub(r"\(\d+(?:,\d+)?\)$", "", value)
    value = re.sub(r"^[a-z]:", "", value)
    # Keep repository-relative/generated suffixes, not machine-specific checkout roots.
    for marker in ("/src/", "/obj/"):
        index = value.find(marker)
        if index >= 0:
            return value[index:]
    return value.lstrip("/")


def warning_fingerprint(code: str, owner: str, line: str) -> str:
    parts = diagnostic_parts(line)
    match = WARNING.search(line)
    message = match.group("message") if match else ""
    message = re.sub(r"0x[0-9a-f]+", "0xADDR", message.lower())
    project = (parts["project"] or "").replace("\\", "/").rsplit("/", 1)[-1].lower()
    identity = "|".join((code.upper(), owner, stable_path(parts["sourcePath"]),
                         project, message.strip()))
    return hashlib.sha256(identity.encode("utf-8")).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("publish_log", type=Path)
    parser.add_argument("report_json", type=Path)
    parser.add_argument("--fail-on-foundgine", action="store_true")
    args = parser.parse_args()
    raw = args.publish_log.read_text(encoding="utf-8", errors="replace") if args.publish_log.exists() else ""
    warnings = []
    for line in raw.splitlines():
        match = WARNING.search(line)
        if not match:
            continue
        code = match.group("code").upper()
        owner, ownership_evidence = classify_owner(line)
        parts = diagnostic_parts(line)
        warnings.append({"code": code, "owner": owner, "ownershipEvidence": ownership_evidence,
                         **parts, "fingerprint": warning_fingerprint(code, owner, line), "line": line.strip()})
    counts = {name: sum(item["owner"] == name for item in warnings)
              for name in ("foundgine", "provider-or-dependency", "toolchain-or-unclassified")}
    blocking = [item for item in warnings if item["owner"] == "foundgine" and item["code"] in {"IL2026", "IL3050"}]
    payload = {"schemaVersion": 4, "sourceLog": args.publish_log.name, "warningCount": len(warnings),
               "countsByOwner": counts,
               "countsBySourceKind": {kind: sum(w["sourceKind"] == kind for w in warnings)
                                      for kind in ("source", "compiler-generated", "project-only", "unknown")},
               "foundgineWarnings": [w for w in warnings if w["owner"] == "foundgine"],
               "blockingFoundgineWarnings": blocking, "warnings": warnings,
               "enforcement": {"enabled": args.fail_on_foundgine,
                               "blockingCodes": ["IL2026", "IL3050"], "blockingCount": len(blocking),
                               "result": "failed" if args.fail_on_foundgine and blocking else "passed-or-not-enforced"},
               "policy": "Ownership uses package source, then consuming project identity, then source path. Generated source is explicitly identified but is not attributed to an authored file without compiler-provided mapping. Ambiguous diagnostics remain unclassified; classification never suppresses compiler output."}
    args.report_json.parent.mkdir(parents=True, exist_ok=True)
    args.report_json.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(f"Classified {len(warnings)} warnings: " + ", ".join(f"{k}={v}" for k, v in counts.items()))
    return 2 if args.fail_on_foundgine and blocking else 0


if __name__ == "__main__":
    raise SystemExit(main())
