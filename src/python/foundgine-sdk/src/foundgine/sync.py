"""Synchronous facade for scripts and notebooks (PY-API-9).

It runs the async core on a private event-loop thread, so it also works inside Jupyter (which already has a
running loop). The methods and exceptions are the same as the async client's. Not available here: ``watch``
(use ``wait_for_completion``) and the ``cancel`` event; interrupt with Ctrl-C instead, which cancels the call.
"""

from __future__ import annotations

import asyncio
import threading
from collections.abc import Coroutine, Generator
from concurrent.futures import Future
from types import TracebackType
from typing import Any, TypeVar

import httpx

from .client import FoundgineClient, Tool, ToolResult
from .http import TokenProvider
from .workflows import WorkflowProcessDescription, WorkflowStartResult, WorkflowStatus

T = TypeVar("T")


class _LoopThread:
    """A daemon thread that owns one event loop. Created lazily, stopped by :meth:`stop`."""

    def __init__(self) -> None:
        self.loop = asyncio.new_event_loop()
        self._thread = threading.Thread(target=self._run, name="foundgine-sync", daemon=True)
        self._thread.start()

    def _run(self) -> None:
        asyncio.set_event_loop(self.loop)
        self.loop.run_forever()
        self.loop.close()

    def run(self, coro: Coroutine[Any, Any, T]) -> T:
        future: Future[T] = asyncio.run_coroutine_threadsafe(coro, self.loop)
        try:
            return future.result()
        except BaseException:
            future.cancel()  # Ctrl-C (or any failure while waiting) cancels the in-flight call
            raise

    def stop(self) -> None:
        if self._thread.is_alive():
            self.loop.call_soon_threadsafe(self.loop.stop)
            self._thread.join(timeout=5)


class SyncWorkflowClient:
    """Blocking counterpart of :class:`foundgine.WorkflowClient` (without ``watch``)."""

    def __init__(self, owner: FoundgineSyncClient) -> None:
        self._owner = owner

    def processes(self) -> list[str]:
        return self._owner._run(self._owner._client.workflows.processes())

    def describe(self, process: str) -> WorkflowProcessDescription:
        return self._owner._run(self._owner._client.workflows.describe(process))

    def start(self, process: str, business_key: str, payload: Any = None) -> WorkflowStartResult:
        return self._owner._run(self._owner._client.workflows.start(process, business_key, payload))

    def status(self, process: str, business_key: str) -> WorkflowStatus:
        return self._owner._run(self._owner._client.workflows.status(process, business_key))

    def decide(self, process: str, business_key: str, step: str, approved: bool, reason: str | None = None) -> None:
        self._owner._run(self._owner._client.workflows.decide(process, business_key, step, approved, reason))

    def cancel(self, process: str, business_key: str) -> None:
        self._owner._run(self._owner._client.workflows.cancel(process, business_key))

    def wait_for_completion(
        self,
        process: str,
        business_key: str,
        *,
        interval: float = 1.0,
        timeout: float | None = None,
    ) -> WorkflowStatus:
        return self._owner._run(
            self._owner._client.workflows.wait_for_completion(process, business_key, interval=interval, timeout=timeout)
        )


class FoundgineSyncClient:
    """Blocking client with the same methods as :class:`foundgine.FoundgineClient`. Use it as a context manager."""

    def __init__(
        self,
        base_url: str,
        token: TokenProvider = None,
        *,
        mcp_path: str | None = None,
        protocol_version: str | None = None,
        timeout: httpx.Timeout | float | None = None,
    ) -> None:
        self._runner = _LoopThread()
        try:
            # Constructed on the loop thread so every asyncio object belongs to that loop.
            self._client: FoundgineClient = self._runner.run(
                _build(base_url, token, mcp_path, protocol_version, timeout)
            )
        except BaseException:
            self._runner.stop()
            raise
        self.workflows = SyncWorkflowClient(self)
        self._closed = False

    @property
    def base_url(self) -> str:
        return self._client.base_url

    def list_capabilities(self) -> list[Tool]:
        return self._run(self._client.list_capabilities())

    def execute(self, name: str, arguments: dict[str, Any] | None = None) -> ToolResult:
        return self._run(self._client.execute(name, arguments))

    def notifications(
        self, *, last_event_id: str | None = None, reconnect_delay: float = 1.0
    ) -> Generator[dict[str, Any], None, None]:
        """Blocking iterator over notifications. Close it (``contextlib.closing``) if you stop early, so the
        server's single stream slot is released."""
        stream = self._client.notifications(last_event_id=last_event_id, reconnect_delay=reconnect_delay)
        try:
            while True:
                try:
                    yield self._run(stream.__anext__())
                except StopAsyncIteration:
                    return
        finally:
            if not self._closed:
                self._run(stream.aclose())

    def close(self) -> None:
        """End the MCP session, close the HTTP client and stop the loop thread. Idempotent."""
        if self._closed:
            return
        try:
            self._run(self._client.close())
        finally:
            self._closed = True
            self._runner.stop()

    def __enter__(self) -> FoundgineSyncClient:
        return self

    def __exit__(
        self, exc_type: type[BaseException] | None, exc: BaseException | None, tb: TracebackType | None
    ) -> None:
        self.close()

    def _run(self, coro: Coroutine[Any, Any, T]) -> T:
        if self._closed:
            coro.close()
            raise RuntimeError("FoundgineSyncClient is closed")
        return self._runner.run(coro)


async def _build(
    base_url: str,
    token: TokenProvider,
    mcp_path: str | None,
    protocol_version: str | None,
    timeout: httpx.Timeout | float | None,  # noqa: ASYNC109 - passed through to httpx
) -> FoundgineClient:
    return FoundgineClient(base_url, token, mcp_path=mcp_path, protocol_version=protocol_version, timeout=timeout)
