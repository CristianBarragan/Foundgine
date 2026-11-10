#!/usr/bin/env python3
"""Validate the ORM provider evidence manifest without treating planned tests as passing."""
import json
from pathlib import Path

path = Path(__file__).with_name("provider-conformance-matrix.json")
data = json.loads(path.read_text(encoding="utf-8"))
assert data.get("schemaVersion") == 1
assert data.get("matrixVersion") == "step42"
providers = data.get("providers")
assert isinstance(providers, list) and {p["id"] for p in providers} == {"efcore-sqlite"}
required = {"implementation", "translation", "runtime", "nativeAotPublish", "nativeAotExecution"}
for provider in providers:
    assert provider.get("provider") and provider.get("database")
    for capability, evidence in provider["capabilities"].items():
        missing = required - evidence.keys()
        assert not missing, f"{provider['id']} / {capability}: missing {sorted(missing)}"
        for key in required:
            assert evidence[key] in data["statusVocabulary"], (
                f"{provider['id']} / {capability} / {key}: unknown status {evidence[key]!r}"
            )
        if capability == "aggregate-cursor-pagination":
            assert evidence.get("gate") == "disabled", "Aggregate cursor pagination must remain gated."
            assert evidence["runtime"] == "not-certified"
print(f"Validated {len(providers)} providers and "
      f"{sum(len(p['capabilities']) for p in providers)} capability records.")
