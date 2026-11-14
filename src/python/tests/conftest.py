from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Any

import pytest


def conformance_dir() -> Path | None:
    """src/conformance, found like the TS tests do: FOUNDGINE_CONFORMANCE_DIR, else the repo's src/conformance."""
    override = os.environ.get("FOUNDGINE_CONFORMANCE_DIR")
    if override:
        return Path(override)
    for parent in Path(__file__).resolve().parents:
        candidate = parent / "conformance"
        if candidate.is_dir():
            return candidate
    return None


def load_vectors(name: str) -> dict[str, Any]:
    """Load a shared vector file. Missing vectors skip locally but FAIL in CI."""
    base = conformance_dir()
    path = base / name if base else None
    if path is None or not path.is_file():
        message = f"conformance vectors not found: {name} (looked in {base})"
        if os.environ.get("CI"):
            pytest.fail(message)
        pytest.skip(message)
    assert path is not None
    data: dict[str, Any] = json.loads(path.read_text(encoding="utf-8"))
    return data
