"""Shared SSE vectors (src/conformance/mcp-sse-scenarios.json), as the TS conformance test runs them."""

from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any

import pytest
from conftest import load_vectors

from foundgine import SseEvent, SseParser


def format_steps(doc: dict[str, Any]) -> list[dict[str, Any]]:
    return [s for sc in doc["scenarios"] for s in sc["steps"] if s.get("op") == "format"]


def normalised(data: str) -> str:
    # The server splits payloads on \r\n, \r and \n; a compliant reader joins the data lines with \n.
    return re.sub(r"\r\n|\r", "\n", data)


def check_vector(step: dict[str, Any]) -> None:
    frame, data = step["expect"], step["data"]
    want = [SseEvent(id=step["sequence"], event=None, data=normalised(data))]
    assert SseParser().push(frame) == want
    p = SseParser()
    got: list[SseEvent] = []
    for ch in frame:  # 1-character chunks, as TCP/TextDecoder boundaries can produce
        got += p.push(ch)
    assert got == want
    p = SseParser()
    got = []
    raw = frame.encode()
    for i in range(len(raw)):  # and 1-byte chunks through the incremental decoder
        got += p.push(raw[i : i + 1])
    assert got == want


def test_sse_frame_vectors_whole_and_chunked() -> None:
    steps = format_steps(load_vectors("mcp-sse-scenarios.json"))
    assert len(steps) >= 7, "expected the frame-format vectors"
    for step in steps:
        check_vector(step)


def test_all_frame_vectors_in_one_stream_keep_order_and_ids() -> None:
    steps = format_steps(load_vectors("mcp-sse-scenarios.json"))
    got = SseParser().push("".join(s["expect"] for s in steps))
    assert [e.id for e in got] == [s["sequence"] for s in steps]
    assert all(e.event is None for e in got), "an injected 'event:' line in a payload must never become a field"


# ---- the loader and checker themselves, against a synthetic vector file in the same shape ----


def server_frame(seq: str, data: str) -> str:
    """What the server emits: `id:` then one `data:` line per payload line (split on \\r\\n, \\r, \\n)."""
    lines = re.split(r"\r\n|\r|\n", data)
    return f"id: {seq}\n" + "".join(f"data: {ln}\n" for ln in lines) + "\n"


def synthetic(payloads: list[str]) -> dict[str, Any]:
    return {
        "scenarios": [
            {
                "steps": [
                    {"op": "format", "sequence": str(i), "data": d, "expect": server_frame(str(i), d)}
                    for i, d in enumerate(payloads, 1)
                ]
            }
        ]
    }


PAYLOADS = [
    '{"a":1}',
    "line1\nline2",
    "crlf1\r\ncrlf2",
    "cr1\rcr2",
    "id: 5",
    "event: x\ndata: y",
    "ends with newline\n",
    "u2028 \u2028 u0085 \x85 vt \x0b",
    "emoji \U0001f600 \u4e16\u754c",
    "",
]


def test_checker_accepts_synthetic_vectors_in_the_server_format() -> None:
    for step in format_steps(synthetic(PAYLOADS)):
        check_vector(step)


def test_loader_reads_the_override_directory_and_fails_in_ci_when_missing(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    (tmp_path / "mcp-sse-scenarios.json").write_text(json.dumps(synthetic(PAYLOADS)), encoding="utf-8")
    monkeypatch.setenv("FOUNDGINE_CONFORMANCE_DIR", str(tmp_path))
    assert len(format_steps(load_vectors("mcp-sse-scenarios.json"))) == len(PAYLOADS)

    monkeypatch.setenv("FOUNDGINE_CONFORMANCE_DIR", str(tmp_path / "nowhere"))
    monkeypatch.setenv("CI", "true")
    with pytest.raises(pytest.fail.Exception):
        load_vectors("mcp-sse-scenarios.json")
    monkeypatch.delenv("CI")
    with pytest.raises(pytest.skip.Exception):
        load_vectors("mcp-sse-scenarios.json")


# ---- transport vectors (src/conformance/mcp-sse-transport-scenarios.json): every non-200 GET status ----


def gate_statuses(doc: dict[str, Any]) -> set[int]:
    return {
        s["expect"]
        for sc in doc["scenarios"]
        for s in sc["steps"]
        if s.get("op") == "get" and isinstance(s.get("expect"), int) and s["expect"] != 200
    }


async def stream_error_for(status: int) -> Exception:
    import httpx
    from helpers import BASE, Call, accepted, json_response, mock_http, rpc_ok

    from foundgine import FoundgineClient

    def handler(call: Call) -> httpx.Response:
        if call.rpc == "initialize":
            return rpc_ok(call, {}, {"Mcp-Session-Id": "s"})
        if call.rpc == "notifications/initialized":
            return accepted()
        return json_response({"error": f"gate {status}"}, status)

    http, _ = mock_http(handler)
    with pytest.raises(Exception) as info:
        await anext(FoundgineClient(BASE, http_client=http).notifications())
    return info.value


async def test_every_transport_vector_status_is_classified() -> None:
    from foundgine import FoundgineHttpError, FoundgineResyncRequiredError, FoundgineSessionExpiredError

    statuses = gate_statuses(load_vectors("mcp-sse-transport-scenarios.json"))
    assert {400, 403, 404, 409, 410} <= statuses, "expected the gate statuses the TS test covers"
    for status in sorted(statuses):
        error = await stream_error_for(status)
        assert isinstance(error, FoundgineHttpError) and error.status == status
        assert isinstance(error, FoundgineSessionExpiredError) == (status == 404)
        assert isinstance(error, FoundgineResyncRequiredError) == (status == 410)
