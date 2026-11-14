"""Ports of the TS workflow tests."""

from __future__ import annotations

import asyncio
import gc
import logging
import warnings
from collections.abc import Callable
from typing import Any
from urllib.parse import unquote

import httpx
import pytest
from helpers import BASE, Call, json_response, mock_http

from foundgine import (
    FoundgineAbortError,
    FoundgineClient,
    FoundgineError,
    FoundgineHttpError,
    FoundgineWatchTimeoutError,
    WorkflowStatus,
)


def fixed_body(body: str) -> Callable[[Call], httpx.Response]:
    return lambda call: httpx.Response(200, content=body)


def status_json(state: str = "Running", **process: Any) -> httpx.Response:
    return json_response({"workflowId": "wf-1", "temporalStatus": state, "process": process or None})


async def test_routes_encode_segments_authenticate_and_keep_the_decision_body_shape() -> None:
    def handler(call: Call) -> httpx.Response:
        if call.method == "POST" and call.url.endswith(("/decision", "/cancel")):
            return httpx.Response(202)
        if call.method == "POST":
            return json_response({"workflowId": "wf-1", "status": "Started", "alreadyExisted": False}, 202)
        return json_response({"workflowId": "wf-1", "temporalStatus": "Running", "process": {"state": "Waiting"}})

    http, calls = mock_http(handler)
    client = FoundgineClient(BASE, "secret", http_client=http)
    started = await client.workflows.start("order/approve", "key ?# ü", {"amount": 3})
    assert (started.workflow_id, started.status, started.already_existed) == ("wf-1", "Started", False)
    await client.workflows.decide("order/approve", "key ?# ü", "approve", True)
    await client.workflows.cancel("order/approve", "key ?# ü")
    status = await client.workflows.status("order/approve", "key ?# ü")
    assert status.temporal_status == "Running" and status.process == {"state": "Waiting"}
    assert calls[0].url == f"{BASE}/foundgine/workflows/order%2Fapprove"
    assert calls[0].body == {"businessKey": "key ?# ü", "payload": {"amount": 3}}
    assert calls[3].url.endswith("/order%2Fapprove/key%20%3F%23%20%C3%BC")
    assert unquote(calls[3].url.rsplit("/", 1)[1]) == "key ?# ü"
    assert calls[1].url.endswith("/decision") and calls[1].body == {"step": "approve", "approved": True}
    assert calls[2].url.endswith("/cancel") and calls[2].body is None
    assert all(c.headers["authorization"] == "Bearer secret" for c in calls)


async def test_decide_sends_reason_only_when_given_and_never_an_identity() -> None:
    http, calls = mock_http(lambda call: httpx.Response(202))
    client = FoundgineClient(BASE, http_client=http)
    await client.workflows.decide("p", "k", "s", False, "too risky")
    assert calls[0].body == {"step": "s", "approved": False, "reason": "too risky"}
    assert not {"decidedBy", "subject", "user"} & set(calls[0].body)


async def test_start_defaults_payload_to_empty_object_and_reports_existing() -> None:
    existing = {"workflowId": "w", "status": "Existing", "alreadyExisted": True}
    http, calls = mock_http(lambda call: json_response(existing, 200))
    result = await FoundgineClient(BASE, http_client=http).workflows.start("p", "k")
    assert calls[0].body == {"businessKey": "k", "payload": {}}
    assert result.status == "Existing" and result.already_existed is True


async def test_http_errors_are_typed_and_malformed_bodies_are_foundgine_errors() -> None:
    http, _ = mock_http(lambda call: json_response({"error": "nope"}, 404))
    with pytest.raises(FoundgineHttpError) as info:
        await FoundgineClient(BASE, http_client=http).workflows.status("p", "k")
    assert info.value.status == 404 and type(info.value) is FoundgineHttpError  # not SessionExpired

    for body, message in [("not json", "invalid JSON"), ("[1]", "non-object"), ('{"workflowId":"w"}', "missing")]:
        http, _ = mock_http(fixed_body(body))
        with pytest.raises(FoundgineError, match=message):
            await FoundgineClient(BASE, http_client=http).workflows.status("p", "k")


async def test_watch_returns_immediately_replays_distinct_updates_and_resolves() -> None:
    count = 0

    def handler(call: Call) -> httpx.Response:
        nonlocal count
        count += 1
        return status_json("Running", step=count) if count < 3 else status_json("Completed", step=2)

    http, calls = mock_http(handler)
    client = FoundgineClient(BASE, http_client=http)
    loop = asyncio.get_running_loop()
    began = loop.time()
    watch = client.workflows.watch("order", "k", interval=0.001)
    assert loop.time() - began < 0.05 and calls == []  # no I/O before the handle exists
    seen = [u.temporal_status async for u in watch.updates()]
    done = await watch.done
    assert done.temporal_status == "Completed"
    assert seen == ["Running", "Running", "Completed"]
    assert watch.is_finished and watch.latest == done


async def test_updates_replay_for_a_late_consumer() -> None:
    http, _ = mock_http(lambda call: status_json("Completed"))
    watch = FoundgineClient(BASE, http_client=http).workflows.watch("p", "k")
    await watch.done
    assert [u.temporal_status async for u in watch.updates()] == ["Completed"]


async def test_stop_rejects_done_and_updates_with_the_abort_error() -> None:
    http, _ = mock_http(lambda call: status_json("Running"))
    watch = FoundgineClient(BASE, http_client=http).workflows.watch("p", "k", interval=10)
    await asyncio.sleep(0)
    await asyncio.sleep(0.01)
    watch.stop()
    with pytest.raises(FoundgineAbortError):
        await watch.done
    with pytest.raises(FoundgineAbortError):
        async for _ in watch.updates():
            pass


async def test_no_request_is_sent_after_stop() -> None:
    http, calls = mock_http(lambda call: status_json("Running"))
    watch = FoundgineClient(BASE, http_client=http).workflows.watch("p", "k", interval=0.01)
    await asyncio.sleep(0.03)
    watch.stop()
    await asyncio.gather(watch.done, return_exceptions=True)
    sent = len(calls)
    await asyncio.sleep(0.05)
    assert len(calls) == sent


async def test_external_cancel_event_stops_the_watch() -> None:
    http, _ = mock_http(lambda call: status_json("Running"))
    cancel = asyncio.Event()
    watch = FoundgineClient(BASE, http_client=http).workflows.watch("p", "k", interval=10, cancel=cancel)
    await asyncio.sleep(0.01)
    cancel.set()
    with pytest.raises(FoundgineAbortError):
        await watch.done
    with pytest.raises(FoundgineAbortError):
        async for _ in watch.updates():
            pass


async def test_async_with_stops_the_watch_on_exit() -> None:
    http, _ = mock_http(lambda call: status_json("Running"))
    async with FoundgineClient(BASE, http_client=http).workflows.watch("p", "k", interval=10) as watch:
        await asyncio.sleep(0.01)
    assert watch.is_finished
    with pytest.raises(FoundgineAbortError):
        await watch.done


async def test_stopping_an_unawaited_watch_logs_no_unretrieved_exception() -> None:
    loop = asyncio.get_running_loop()
    reports: list[dict[str, Any]] = []
    loop.set_exception_handler(lambda _loop, context: reports.append(context))
    http, _ = mock_http(lambda call: status_json("Running"))
    watch = FoundgineClient(BASE, http_client=http).workflows.watch("p", "k", interval=10)
    await asyncio.sleep(0.01)
    watch.stop()
    await asyncio.sleep(0.01)
    del watch
    with warnings.catch_warnings():
        warnings.simplefilter("error")
        gc.collect()
    await asyncio.sleep(0)
    assert reports == []


async def test_wait_for_completion_returns_the_terminal_status() -> None:
    http, _ = mock_http(lambda call: status_json("Completed"))
    result = await FoundgineClient(BASE, http_client=http).workflows.wait_for_completion("p", "k", interval=0.001)
    assert result.temporal_status == "Completed"


async def test_unknown_statuses_are_not_terminal_and_case_is_ignored() -> None:
    states = iter(["ContinuedAsNew", "completed"])
    http, _ = mock_http(lambda call: status_json(next(states)))
    result = await FoundgineClient(BASE, http_client=http).workflows.wait_for_completion("p", "k", interval=0.001)
    assert result.temporal_status == "completed"


async def test_watch_tolerates_the_initial_not_found_window_and_isolates_callback_failures(
    caplog: pytest.LogCaptureFixture,
) -> None:
    count = 0
    updates = 0

    def handler(call: Call) -> httpx.Response:
        nonlocal count
        count += 1
        if count == 1:
            return json_response({"error": "not visible yet"}, 404)
        return status_json("Running", step=1) if count < 4 else status_json("Completed", step=2)

    def boom(_: WorkflowStatus) -> None:
        nonlocal updates
        updates += 1
        raise RuntimeError("consumer callback failure")

    http, _ = mock_http(handler)
    with caplog.at_level(logging.DEBUG, logger="foundgine"):
        watch = FoundgineClient(BASE, http_client=http).workflows.watch(
            "order", "k", interval=0.001, not_found_grace=0.1, on_update=boom
        )
        result = await watch.done
    assert result.temporal_status == "Completed"
    assert updates == 2, "duplicate snapshots are not emitted to on_update"


async def test_async_callbacks_are_awaited() -> None:
    seen: list[str] = []

    async def on_update(status: WorkflowStatus) -> None:
        await asyncio.sleep(0)
        seen.append(status.temporal_status)

    http, _ = mock_http(lambda call: status_json("Completed"))
    await FoundgineClient(BASE, http_client=http).workflows.watch("p", "k", on_update=on_update).done
    assert seen == ["Completed"]


async def test_watch_retries_transient_errors_then_gives_up_with_the_last_error() -> None:
    errors: list[int] = []
    http, calls = mock_http(lambda call: json_response({"error": "down"}, 503))
    watch = FoundgineClient(BASE, http_client=http).workflows.watch(
        "p", "k", interval=0.001, max_consecutive_errors=2, on_error=lambda e, n: errors.append(n)
    )
    with pytest.raises(FoundgineHttpError) as info:
        await watch.done
    assert info.value.status == 503 and errors == [1, 2, 3] and len(calls) == 3


async def test_a_404_after_the_grace_period_is_fatal() -> None:
    http, _ = mock_http(lambda call: json_response({"error": "gone"}, 404))
    watch = FoundgineClient(BASE, http_client=http).workflows.watch(
        "p", "k", interval=0.001, not_found_grace=0, max_consecutive_errors=0
    )
    await asyncio.sleep(0.01)
    with pytest.raises(FoundgineHttpError) as info:
        await watch.done
    assert info.value.status == 404


async def test_watch_timeout_raises_with_the_last_status() -> None:
    http, _ = mock_http(lambda call: status_json("Running", step="waiting"))
    watch = FoundgineClient(BASE, http_client=http).workflows.watch("order", "k", interval=0.001, timeout=0.01)
    with pytest.raises(FoundgineWatchTimeoutError) as info:
        await watch.done
    assert info.value.last_status.temporal_status == "Running"


async def test_wait_for_completion_timeout_carries_the_latest_status() -> None:
    http, _ = mock_http(lambda call: status_json("Running", step="waiting"))
    with pytest.raises(FoundgineWatchTimeoutError) as info:
        await FoundgineClient(BASE, http_client=http).workflows.wait_for_completion(
            "order", "k", interval=0.001, timeout=0.01
        )
    assert info.value.last_status.temporal_status == "Running"


async def test_wait_for_completion_honours_the_cancel_event() -> None:
    http, _ = mock_http(lambda call: status_json("Running"))
    cancel = asyncio.Event()
    task = asyncio.ensure_future(
        FoundgineClient(BASE, http_client=http).workflows.wait_for_completion("p", "k", interval=10, cancel=cancel)
    )
    await asyncio.sleep(0.01)
    cancel.set()
    with pytest.raises(FoundgineAbortError):
        await task


async def test_start_and_watch() -> None:
    state = {"polls": 0}

    def handler(call: Call) -> httpx.Response:
        if call.method == "POST":
            return json_response({"workflowId": "wf-1", "status": "Started", "alreadyExisted": False}, 202)
        state["polls"] += 1
        return status_json("Running") if state["polls"] < 2 else status_json("Completed")

    http, _ = mock_http(handler)
    started, watch = await FoundgineClient(BASE, http_client=http).workflows.start_and_watch(
        "order-approval", "order-1042", {"orderId": "order-1042"}, interval=0.001
    )
    assert started.workflow_id == "wf-1" and started.status == "Started"
    assert (await watch.done).temporal_status == "Completed"


async def test_a_401_during_watch_is_fatal_at_once_like_the_ts_sdk() -> None:
    http, calls = mock_http(lambda call: json_response({"error": "nope"}, 401))
    watch = FoundgineClient(BASE, http_client=http).workflows.watch("p", "k", interval=0.001)
    with pytest.raises(FoundgineHttpError) as info:
        await watch.done
    assert info.value.status == 401 and len(calls) == 1


async def test_processes_lists_the_catalog_and_validates_it() -> None:
    http, calls = mock_http(lambda call: json_response({"processes": ["a", "b"]}))
    assert await FoundgineClient(BASE, http_client=http).workflows.processes() == ["a", "b"]
    assert calls[0].method == "GET" and calls[0].url == f"{BASE}/foundgine/workflows"
    bad, _ = mock_http(lambda call: json_response({"processes": ["a", 1]}))
    with pytest.raises(FoundgineError):
        await FoundgineClient(BASE, http_client=bad).workflows.processes()


async def test_describe_maps_steps_and_rejects_invalid_descriptions() -> None:
    body = {
        "name": "a/b",
        "steps": [
            {"name": "s1", "requiresApproval": True, "hasCompensation": False, "approvalTimeoutSeconds": 30},
            {"name": "s2"},
        ],
    }
    http, calls = mock_http(lambda call: json_response(body))
    described = await FoundgineClient(BASE, http_client=http).workflows.describe("a/b")
    assert calls[0].url == f"{BASE}/foundgine/workflows/a%2Fb"
    assert described.name == "a/b"
    assert (described.steps[0].requires_approval, described.steps[0].approval_timeout_seconds) == (True, 30)
    assert (described.steps[1].requires_approval, described.steps[1].has_compensation) == (False, False)
    assert described.steps[1].approval_timeout_seconds is None
    broken_bodies: list[dict[str, Any]] = [{"steps": []}, {"name": "x", "steps": [1]}, {"name": "x", "steps": [{}]}]
    for broken in broken_bodies:
        bad, _ = mock_http(lambda call, b=broken: json_response(b))  # type: ignore[misc]
        with pytest.raises(FoundgineError):
            await FoundgineClient(BASE, http_client=bad).workflows.describe("x")
