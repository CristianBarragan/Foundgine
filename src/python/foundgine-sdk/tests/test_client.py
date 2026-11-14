"""Ports of the TS tests in test/sdk.test.mjs that cover the MCP client."""

from __future__ import annotations

import asyncio
import contextlib
from typing import Any

import httpx
import pytest
from helpers import BASE, Call, accepted, json_response, mock_http, rpc_ok

from foundgine import (
    FoundgineAbortError,
    FoundgineClient,
    FoundgineError,
    FoundgineHttpError,
    FoundgineResyncRequiredError,
    FoundgineRpcError,
    FoundgineSessionExpiredError,
)


def handshake(call: Call, session: str | None = "s-1") -> httpx.Response | None:
    if call.rpc == "initialize":
        return rpc_ok(call, {"protocolVersion": "2025-06-18"}, {"Mcp-Session-Id": session} if session else None)
    if call.rpc == "notifications/initialized":
        return accepted()
    return None


async def test_initializes_once_sends_headers_pages_tools_and_executes() -> None:
    counter = 0

    def token() -> str:
        nonlocal counter
        counter += 1
        return f"token-{counter}"

    def handler(call: Call) -> httpx.Response:
        if call.method == "DELETE":
            return httpx.Response(204)
        if (r := handshake(call)) is not None:
            return r
        if call.rpc == "tools/list" and not call.body.get("params", {}).get("cursor"):
            return rpc_ok(call, {"tools": [{"name": "one", "inputSchema": {"type": "object"}}], "nextCursor": "next"})
        if call.rpc == "tools/list":
            return rpc_ok(call, {"tools": [{"name": "two"}]})
        if call.rpc == "tools/call":
            return rpc_ok(call, {"content": [{"type": "text", "text": "ok"}], "isError": False})
        raise AssertionError(f"Unexpected request {call.rpc}")

    http, calls = mock_http(handler)
    async with FoundgineClient(BASE, token, http_client=http) as client:
        tools = await client.list_capabilities()
        result = await client.execute("one", {"q": "x"})
    assert [t.name for t in tools] == ["one", "two"]
    assert tools[0].input_schema == {"type": "object"} and tools[1].input_schema == {}
    assert result.content[0]["text"] == "ok" and result.is_error is False
    assert [c.rpc for c in calls].count("initialize") == 1
    assert all(c.headers["authorization"].startswith("Bearer token-") for c in calls)
    posted = [c for c in calls if c.method == "POST" and c.rpc != "initialize"]
    assert all(c.headers["mcp-session-id"] == "s-1" for c in posted)
    assert all(c.headers["mcp-protocol-version"] == "2025-06-18" for c in posted)
    await http.aclose()


async def test_async_token_provider_and_tool_failure_is_data() -> None:
    async def token() -> str:
        return "async-tok"

    def handler(call: Call) -> httpx.Response:
        if (r := handshake(call)) is not None:
            return r
        return rpc_ok(call, {"content": [], "isError": True, "structuredContent": {"why": "bad"}})

    http, calls = mock_http(handler)
    client = FoundgineClient(BASE, token, http_client=http)
    result = await client.execute("t")
    assert result.is_error and result.structured_content == {"why": "bad"}
    assert all(c.headers["authorization"] == "Bearer async-tok" for c in calls)


async def test_401_is_an_http_error_and_rpc_errors_are_typed() -> None:
    http, _ = mock_http(lambda call: json_response({"error": "no"}, 401))
    with pytest.raises(FoundgineHttpError) as info:
        await FoundgineClient(BASE, http_client=http).list_capabilities()
    assert info.value.status == 401 and "no" in str(info.value)

    def rpc(call: Call) -> httpx.Response:
        if call.rpc == "initialize":
            return rpc_ok(call, {})
        if call.rpc == "notifications/initialized":
            return accepted()
        return json_response({"jsonrpc": "2.0", "id": call.rpc_id, "error": {"code": -32601, "message": "missing"}})

    http2, _ = mock_http(rpc)
    with pytest.raises(FoundgineRpcError) as rpc_info:
        await FoundgineClient(BASE, http_client=http2).mcp.request("not-found")
    assert rpc_info.value.code == -32601


async def test_404_expires_the_session_and_the_next_call_initializes_again() -> None:
    state = {"init": 0, "lists": 0}

    def handler(call: Call) -> httpx.Response:
        if call.rpc == "initialize":
            state["init"] += 1
            return rpc_ok(call, {}, {"Mcp-Session-Id": f"s-{state['init']}"})
        if call.rpc == "notifications/initialized":
            return accepted()
        if call.rpc == "tools/list":
            state["lists"] += 1
            if state["lists"] == 1:
                return json_response({"error": "expired"}, 404)
        return rpc_ok(call, {"tools": []})

    http, _ = mock_http(handler)
    client = FoundgineClient(BASE, http_client=http)
    with pytest.raises(FoundgineSessionExpiredError):
        await client.list_capabilities()
    assert client.mcp.session is None
    await client.list_capabilities()
    assert state["init"] == 2


async def test_concurrent_first_calls_produce_one_initialize() -> None:
    def handler(call: Call) -> httpx.Response:
        return handshake(call) or rpc_ok(call, {"tools": []})

    http, calls = mock_http(handler)
    client = FoundgineClient(BASE, http_client=http)
    await asyncio.gather(*(client.list_capabilities() for _ in range(8)))
    assert [c.rpc for c in calls].count("initialize") == 1


@pytest.mark.parametrize("status", [400, 403, 409, 410, 404])
async def test_stream_gate_statuses(status: int) -> None:
    def handler(call: Call) -> httpx.Response:
        if (r := handshake(call, "gate-session")) is not None:
            return r
        if call.method == "GET":
            return json_response({"error": f"gate {status}"}, status)
        return rpc_ok(call, {"tools": []})

    http, _ = mock_http(handler)
    client = FoundgineClient(BASE, http_client=http)
    expected: type[Exception] = {404: FoundgineSessionExpiredError, 410: FoundgineResyncRequiredError}.get(
        status, FoundgineHttpError
    )
    with pytest.raises(expected) as info:
        await anext(client.notifications())
    assert isinstance(info.value, FoundgineHttpError) and info.value.status == status
    assert client.mcp.session == (None if status == 404 else "gate-session")


def sse(id_: str, payload: dict[str, Any]) -> bytes:
    import json

    return f"id: {id_}\ndata: {json.dumps(payload)}\n\n".encode()


async def test_notification_stream_resumes_with_last_event_id() -> None:
    gets = 0

    def handler(call: Call) -> httpx.Response:
        nonlocal gets
        if (r := handshake(call, "stream-session")) is not None:
            return r
        assert call.method == "GET"
        gets += 1
        id_ = "5" if gets == 1 else "6"
        return httpx.Response(
            200, content=sse(id_, {"method": f"notice-{id_}"}), headers={"content-type": "text/event-stream"}
        )

    http, calls = mock_http(handler)
    client = FoundgineClient(BASE, http_client=http)
    cancel = asyncio.Event()
    seen: list[str] = []
    async for item in client.notifications(cancel=cancel, reconnect_delay=0):
        seen.append(item["method"])
        if len(seen) == 2:
            cancel.set()
    assert seen == ["notice-5", "notice-6"]
    get_calls = [c for c in calls if c.method == "GET"]
    assert "last-event-id" not in get_calls[0].headers
    assert get_calls[1].headers["last-event-id"] == "5"


async def test_caller_supplied_last_event_id_is_sent_on_first_get() -> None:
    def handler(call: Call) -> httpx.Response:
        return handshake(call) or httpx.Response(200, content=b"", headers={"content-type": "text/event-stream"})

    http, calls = mock_http(handler)
    client = FoundgineClient(BASE, http_client=http)
    cancel = asyncio.Event()
    stream = client.notifications(cancel=cancel, last_event_id="41", reconnect_delay=10)
    task = asyncio.ensure_future(anext(stream, None))
    await asyncio.sleep(0.05)
    cancel.set()
    assert await task is None
    assert next(c for c in calls if c.method == "GET").headers["last-event-id"] == "41"


class TrackedStream(httpx.AsyncByteStream):
    def __init__(self) -> None:
        self.closed = False

    async def __aiter__(self) -> Any:
        yield sse("1", {"method": "first"})
        await asyncio.sleep(3600)
        yield b""

    async def aclose(self) -> None:
        self.closed = True


async def test_leaving_the_stream_early_closes_the_http_response() -> None:
    stream = TrackedStream()

    def handler(call: Call) -> httpx.Response:
        return handshake(call) or httpx.Response(200, stream=stream, headers={"content-type": "text/event-stream"})

    http, _ = mock_http(handler)
    client = FoundgineClient(BASE, http_client=http)
    async with contextlib.aclosing(client.notifications()) as notes:
        async for item in notes:
            assert item["method"] == "first"
            break
    assert stream.closed


async def test_cancelling_the_consuming_task_closes_the_stream() -> None:
    stream = TrackedStream()

    def handler(call: Call) -> httpx.Response:
        return handshake(call) or httpx.Response(200, stream=stream, headers={"content-type": "text/event-stream"})

    http, _ = mock_http(handler)
    client = FoundgineClient(BASE, http_client=http)

    async def consume() -> None:
        async for _ in client.notifications():
            pass

    task = asyncio.ensure_future(consume())
    await asyncio.sleep(0.05)
    task.cancel()
    with pytest.raises(asyncio.CancelledError):
        await task
    assert stream.closed


def hanging_handler(started: asyncio.Event) -> Any:
    async def handler(call: Call) -> httpx.Response:
        if (r := handshake(call, "cancel-session")) is not None:
            return r
        if call.rpc == "tools/list":
            started.set()
            await asyncio.sleep(3600)
        if call.rpc == "notifications/cancelled":
            return accepted()
        raise AssertionError("unexpected request")

    return handler


async def test_cancel_event_aborts_an_in_flight_request_and_sends_cancelled() -> None:
    started, cancel = asyncio.Event(), asyncio.Event()
    http, calls = mock_http(hanging_handler(started))
    client = FoundgineClient(BASE, http_client=http)
    pending = asyncio.ensure_future(client.mcp.request("tools/list", {}, cancel=cancel))
    await started.wait()
    cancel.set()
    with pytest.raises(FoundgineAbortError):
        await pending
    cancelled = [c for c in calls if c.rpc == "notifications/cancelled"]
    assert len(cancelled) == 1 and cancelled[0].body["params"]["reason"] == "cancelled by client"


async def test_task_cancellation_propagates_and_still_sends_cancelled() -> None:
    started = asyncio.Event()
    http, calls = mock_http(hanging_handler(started))
    client = FoundgineClient(BASE, http_client=http)
    pending = asyncio.ensure_future(client.mcp.request("tools/list", {}))
    await started.wait()
    pending.cancel()
    with pytest.raises(asyncio.CancelledError):
        await pending
    await asyncio.sleep(0.05)
    assert any(c.rpc == "notifications/cancelled" for c in calls)


async def test_an_already_set_cancel_event_fails_fast() -> None:
    http, calls = mock_http(lambda call: pytest.fail("no request expected"))
    cancel = asyncio.Event()
    cancel.set()
    with pytest.raises(FoundgineAbortError):
        await FoundgineClient(BASE, http_client=http).execute("t", cancel=cancel)
    assert calls == []


async def test_sse_framed_rpc_response_is_parsed() -> None:
    def handler(call: Call) -> httpx.Response:
        if (r := handshake(call)) is not None:
            return r
        data = f'{{"jsonrpc":"2.0","id":{call.rpc_id},"result":{{"tools":[{{"name":"streamed"}}]}}}}'
        return httpx.Response(
            200, content=f"data: {data}\n\n", headers={"content-type": "text/event-stream; charset=utf-8"}
        )

    http, _ = mock_http(handler)
    assert [t.name for t in await FoundgineClient(BASE, http_client=http).list_capabilities()] == ["streamed"]


async def test_400_without_a_session_is_a_plain_http_error() -> None:
    http, _ = mock_http(lambda call: json_response({"error": "Mcp-Session-Id required"}, 400))
    with pytest.raises(FoundgineHttpError) as info:
        await anext(FoundgineClient(BASE, http_client=http).notifications())
    assert info.value.status == 400


async def test_token_never_appears_in_error_text() -> None:
    http, _ = mock_http(lambda call: json_response({"error": "denied"}, 403))
    client = FoundgineClient(BASE, "super-secret-token", http_client=http)
    with pytest.raises(FoundgineHttpError) as info:
        await client.list_capabilities()
    for text in (str(info.value), repr(info.value), repr(info.value.body)):
        assert "super-secret-token" not in text and "Bearer" not in text


async def test_transport_failure_becomes_a_foundgine_error() -> None:
    def handler(call: Call) -> httpx.Response:
        raise httpx.ConnectError("boom")

    http, _ = mock_http(handler)
    with pytest.raises(FoundgineError, match="initialization failed"):
        await FoundgineClient(BASE, http_client=http).list_capabilities()


async def test_close_is_idempotent_best_effort_and_leaves_a_supplied_client_open() -> None:
    def handler(call: Call) -> httpx.Response:
        if call.method == "DELETE":
            return json_response({"error": "gone"}, 500)  # teardown failure must be swallowed
        return handshake(call) or rpc_ok(call, {"tools": []})

    http, calls = mock_http(handler)
    client = FoundgineClient(BASE, http_client=http)
    await client.list_capabilities()
    await client.close()
    await client.close()
    assert [c.method for c in calls].count("DELETE") == 1
    assert not http.is_closed


async def test_client_closes_the_http_client_it_created() -> None:
    client = FoundgineClient(BASE)
    inner = client._http
    await client.close()
    assert inner.is_closed


def test_constructor_validates_base_url_and_does_not_follow_redirects() -> None:
    with pytest.raises(ValueError, match="http"):
        FoundgineClient("ftp://nope")
    client = FoundgineClient("https://x.test/")
    assert client.base_url == "https://x.test"
    assert client._http.follow_redirects is False
