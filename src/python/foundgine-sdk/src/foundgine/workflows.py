"""Temporal workflow HTTP client with polling/watch helpers. Mirrors ``workflows.ts``.

Durations are in **seconds** (TypeScript uses milliseconds).
"""

from __future__ import annotations

import asyncio
import contextlib
import inspect
import json
import logging
import time
from collections.abc import AsyncIterator, Awaitable, Callable
from dataclasses import dataclass
from typing import Any, NamedTuple
from urllib.parse import quote

import httpx

from .errors import (
    FoundgineAbortError,
    FoundgineError,
    FoundgineHttpError,
    FoundgineWatchTimeoutError,
)
from .http import USER_AGENT, TokenProvider, await_or_cancel, ensure_success, join_url, resolve_token

logger = logging.getLogger("foundgine")

TERMINAL_STATUSES = frozenset({"COMPLETED", "FAILED", "CANCELED", "CANCELLED", "TERMINATED", "TIMED_OUT"})
"""Compared case-insensitively. Unknown statuses are never terminal, so they cannot end a watch early."""

UpdateCallback = Callable[["WorkflowStatus"], "Awaitable[None] | None"]
ErrorCallback = Callable[[BaseException, int], "Awaitable[None] | None"]


@dataclass(frozen=True, slots=True)
class WorkflowStartResult:
    workflow_id: str
    status: str
    already_existed: bool
    raw: dict[str, Any]


@dataclass(frozen=True, slots=True)
class WorkflowStepDescription:
    """Public metadata for one step. ``approval_timeout_seconds`` is None when the step has no timeout."""

    name: str
    requires_approval: bool
    has_compensation: bool
    approval_timeout_seconds: float | None


@dataclass(frozen=True, slots=True)
class WorkflowProcessDescription:
    name: str
    steps: list[WorkflowStepDescription]


@dataclass(frozen=True, slots=True)
class WorkflowStatus:
    workflow_id: str
    temporal_status: str
    process: Any
    raw: dict[str, Any]


class StartAndWatch(NamedTuple):
    started: WorkflowStartResult
    watch: WorkflowWatch


def is_finished(status: WorkflowStatus) -> bool:
    return status.temporal_status.upper() in TERMINAL_STATUSES


def _segment(value: str) -> str:
    return quote(value, safe="")  # nothing left unescaped: "/", "?", "#" and non-ASCII cannot change the route


async def _call(callback: Callable[..., Any] | None, *args: Any) -> None:
    """Run a user callback (sync or async); its failures never reach the poll loop."""
    if callback is None:
        return
    try:
        result = callback(*args)
        if inspect.isawaitable(result):
            await result
    except Exception:
        logger.debug("workflow callback raised (ignored)", exc_info=True)


class WorkflowWatch:
    """Handle for a background poll. Create it with :meth:`WorkflowClient.watch` inside a running loop.

    * ``done``: awaitable of the final status (raises on timeout, fatal error or stop).
    * ``updates()``: async iterator that replays every distinct observed state from the start.
    * ``stop()``: stop polling; ``done`` and ``updates()`` then raise :class:`FoundgineAbortError`.
    * ``async with`` stops the watch on exit.
    """

    def __init__(self) -> None:
        self._abort = asyncio.Event()
        self._history: list[WorkflowStatus] = []
        self._waiters: set[asyncio.Future[None]] = set()
        self._finished = False
        self._stopped = False
        self._task: asyncio.Task[WorkflowStatus] | None = None

    @property
    def done(self) -> asyncio.Task[WorkflowStatus]:
        assert self._task is not None
        return self._task

    @property
    def is_finished(self) -> bool:
        return self._finished

    @property
    def latest(self) -> WorkflowStatus | None:
        return self._history[-1] if self._history else None

    def stop(self) -> None:
        """Abort polling. No request is sent afterwards. A no-op once the watch has finished."""
        if not self._finished and not self._abort.is_set():
            self._stopped = True
            self._abort.set()

    async def updates(self) -> AsyncIterator[WorkflowStatus]:
        index = 0
        while True:
            while index < len(self._history):
                yield self._history[index]
                index += 1
            if self._finished:
                if self._stopped or self._abort.is_set():
                    raise FoundgineAbortError()
                return
            waiter: asyncio.Future[None] = asyncio.get_running_loop().create_future()
            self._waiters.add(waiter)
            try:
                await waiter
            finally:
                self._waiters.discard(waiter)

    async def __aenter__(self) -> WorkflowWatch:
        return self

    async def __aexit__(self, *exc: object) -> None:
        self.stop()
        if self._task is not None:
            await asyncio.gather(self._task, return_exceptions=True)

    # -- used by WorkflowClient --------------------------------------------------------------------------

    def _push(self, status: WorkflowStatus) -> bool:
        if self._history and self._history[-1].raw == status.raw:
            return False
        self._history.append(status)
        self._wake()
        return True

    def _wake(self) -> None:
        for waiter in self._waiters:
            if not waiter.done():
                waiter.set_result(None)
        self._waiters.clear()

    def _finish(self) -> None:
        self._finished = True
        self._wake()


class WorkflowClient:
    """Authenticated HTTP client for Foundgine's Temporal workflow routes (``/foundgine/workflows``)."""

    def __init__(self, base_url: str, http_client: httpx.AsyncClient, token: TokenProvider = None) -> None:
        self._base_url = base_url
        self._http = http_client
        self._token = token

    async def processes(self) -> list[str]:
        """List registered process names for the authenticated tenant."""
        raw = await self._request("GET", "")
        names = raw.get("processes")
        if not isinstance(names, list) or any(not isinstance(name, str) for name in names):
            raise FoundgineError("Workflow catalog response is missing processes")
        return names

    async def describe(self, process: str) -> WorkflowProcessDescription:
        """Describe public process metadata; operation identifiers are never returned by the server."""
        raw = await self._request("GET", f"/{_segment(process)}")
        name, items = raw.get("name"), raw.get("steps")
        if not isinstance(name, str) or not isinstance(items, list):
            raise FoundgineError("Workflow process description is invalid")
        steps: list[WorkflowStepDescription] = []
        for item in items:
            if not isinstance(item, dict):
                raise FoundgineError("Workflow step description is invalid")
            if not isinstance(item.get("name"), str):
                raise FoundgineError("Workflow step description is missing name")
            timeout = item.get("approvalTimeoutSeconds")
            steps.append(
                WorkflowStepDescription(
                    name=item["name"],
                    requires_approval=item.get("requiresApproval") is True,
                    has_compensation=item.get("hasCompensation") is True,
                    approval_timeout_seconds=timeout
                    if isinstance(timeout, int | float) and not isinstance(timeout, bool)
                    else None,
                )
            )
        return WorkflowProcessDescription(name, steps)

    async def start(self, process: str, business_key: str, payload: Any = None) -> WorkflowStartResult:
        raw = await self._request(
            "POST",
            f"/{_segment(process)}",
            {"businessKey": business_key, "payload": {} if payload is None else payload},
        )
        workflow_id = raw.get("workflowId")
        if not isinstance(workflow_id, str):
            raise FoundgineError("Workflow start returned no workflowId")
        status = raw.get("status")
        return WorkflowStartResult(
            workflow_id=workflow_id,
            status=status if isinstance(status, str) else "Started",
            already_existed=raw.get("alreadyExisted") is True,
            raw=raw,
        )

    async def status(self, process: str, business_key: str) -> WorkflowStatus:
        return await self._status(process, business_key)

    async def decide(
        self, process: str, business_key: str, step: str, approved: bool, reason: str | None = None
    ) -> None:
        """Record an approval decision. There is deliberately no caller-identity argument: the server
        stamps the decision subject from its trusted security context."""
        body: dict[str, Any] = {"step": step, "approved": approved}
        if reason is not None:
            body["reason"] = reason
        await self._request("POST", f"/{_segment(process)}/{_segment(business_key)}/decision", body, empty=True)

    async def cancel(self, process: str, business_key: str) -> None:
        await self._request("POST", f"/{_segment(process)}/{_segment(business_key)}/cancel", empty=True)

    async def wait_for_completion(
        self,
        process: str,
        business_key: str,
        *,
        interval: float = 1.0,
        timeout: float | None = None,  # noqa: ASYNC109 - seconds of polling, not a cancel scope
        cancel: asyncio.Event | None = None,
    ) -> WorkflowStatus:
        """Poll until the workflow is finished. Raises :class:`FoundgineWatchTimeoutError` (carrying the
        last status) on timeout and :class:`FoundgineAbortError` when ``cancel`` is set."""
        started = time.monotonic()
        while True:
            if cancel is not None and cancel.is_set():
                raise FoundgineAbortError()
            last = await await_or_cancel(self._status(process, business_key), cancel)
            if is_finished(last):
                return last
            if timeout is not None and time.monotonic() - started >= timeout:
                raise FoundgineWatchTimeoutError(last)
            await _sleep(interval, cancel)

    def watch(
        self,
        process: str,
        business_key: str,
        *,
        interval: float = 1.0,
        timeout: float | None = None,
        not_found_grace: float = 5.0,
        max_consecutive_errors: int = 5,
        on_update: UpdateCallback | None = None,
        on_error: ErrorCallback | None = None,
        cancel: asyncio.Event | None = None,
    ) -> WorkflowWatch:
        """Start polling in the background and return immediately. Must be called inside a running loop."""
        handle = WorkflowWatch()
        relay: asyncio.Task[None] | None = None
        if cancel is not None:
            relay = asyncio.ensure_future(_relay(cancel, handle._abort))

        async def run() -> WorkflowStatus:
            try:
                return await self._poll(
                    process,
                    business_key,
                    handle,
                    interval,
                    timeout,
                    not_found_grace,
                    max_consecutive_errors,
                    on_update,
                    on_error,
                )
            finally:
                handle._finish()
                if relay is not None:
                    relay.cancel()

        task = asyncio.ensure_future(run())
        # Mark a failure as observed so an unawaited, stopped watch never logs "exception was never retrieved".
        # Awaiting ``done`` still raises the original error.
        task.add_done_callback(lambda t: t.cancelled() or t.exception())
        handle._task = task
        return handle

    async def start_and_watch(
        self, process: str, business_key: str, payload: Any = None, **watch_options: Any
    ) -> StartAndWatch:
        started = await self.start(process, business_key, payload)
        return StartAndWatch(started, self.watch(process, business_key, **watch_options))

    # -- internals ---------------------------------------------------------------------------------------

    async def _status(self, process: str, business_key: str, cancel: asyncio.Event | None = None) -> WorkflowStatus:
        raw = await self._request("GET", f"/{_segment(process)}/{_segment(business_key)}", cancel=cancel)
        workflow_id, temporal_status = raw.get("workflowId"), raw.get("temporalStatus")
        if not isinstance(workflow_id, str) or not isinstance(temporal_status, str):
            raise FoundgineError("Workflow status response is missing workflowId or temporalStatus")
        return WorkflowStatus(workflow_id, temporal_status, raw.get("process"), raw)

    async def _poll(
        self,
        process: str,
        key: str,
        handle: WorkflowWatch,
        interval: float,
        timeout: float | None,  # noqa: ASYNC109
        grace: float,
        max_errors: int,
        on_update: UpdateCallback | None,
        on_error: ErrorCallback | None,
    ) -> WorkflowStatus:
        started = time.monotonic()
        latest: WorkflowStatus | None = None
        errors = 0
        while True:
            if handle._abort.is_set():
                raise FoundgineAbortError()
            try:
                status = await self._status(process, key, handle._abort)
                latest = status
                errors = 0
                if handle._push(status):
                    await _call(on_update, status)
                if is_finished(status):
                    return status
            except FoundgineAbortError:
                raise
            except (FoundgineError, httpx.HTTPError) as error:
                if isinstance(error, FoundgineHttpError) and error.status == 401:
                    raise  # credentials are rejected; retrying cannot help (matches the TS SDK)
                if (
                    isinstance(error, FoundgineHttpError)
                    and error.status == 404
                    and time.monotonic() - started <= grace
                ):
                    pass  # a just-started workflow may not be visible to the status endpoint yet
                else:
                    errors += 1
                    await _call(on_error, error, errors)
                    if errors > max_errors:
                        raise
            if timeout is not None and time.monotonic() - started >= timeout:
                raise FoundgineWatchTimeoutError(latest)
            await _sleep(interval, handle._abort)

    async def _request(
        self,
        method: str,
        path: str,
        body: Any = None,
        *,
        empty: bool = False,
        cancel: asyncio.Event | None = None,
    ) -> dict[str, Any]:
        headers = {"Accept": "application/json", "User-Agent": USER_AGENT}
        token = await resolve_token(self._token)
        if token:
            headers["Authorization"] = f"Bearer {token}"
        content: str | None = None
        if body is not None:
            headers["Content-Type"] = "application/json"
            content = json.dumps(body)
        url = join_url(self._base_url, f"/foundgine/workflows{path}")
        try:
            response = await await_or_cancel(self._http.request(method, url, headers=headers, content=content), cancel)
        except httpx.TransportError as exc:
            raise FoundgineError("The workflow request failed before an HTTP response was received") from exc
        await ensure_success(response)
        if empty or response.status_code == 204:
            return {}
        text = response.text
        if not text.strip():
            return {}
        try:
            value = json.loads(text)
        except ValueError as exc:
            raise FoundgineError("Workflow endpoint returned invalid JSON") from exc
        if not isinstance(value, dict):
            raise FoundgineError("Workflow endpoint returned a non-object JSON response")
        return value


async def _relay(source: asyncio.Event, target: asyncio.Event) -> None:
    await source.wait()
    target.set()


async def _sleep(seconds: float, cancel: asyncio.Event | None) -> None:
    """Sleep for ``seconds``, or raise :class:`FoundgineAbortError` as soon as ``cancel`` is set."""
    if cancel is None:
        await asyncio.sleep(seconds)
        return
    if cancel.is_set():
        raise FoundgineAbortError()
    with contextlib.suppress(TimeoutError):
        await asyncio.wait_for(cancel.wait(), seconds)
        raise FoundgineAbortError()
