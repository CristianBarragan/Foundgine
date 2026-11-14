"""Low-level MCP-over-HTTP session. Mirrors ``mcp.ts``."""

from __future__ import annotations

import asyncio
import contextlib
import json
import logging
from collections.abc import AsyncGenerator
from typing import Any, cast

import httpx

from .errors import (
    FoundgineAbortError,
    FoundgineError,
    FoundgineResyncRequiredError,
    FoundgineRpcError,
    FoundgineSessionExpiredError,
)
from .http import USER_AGENT, TokenProvider, await_or_cancel, error_for, join_url, resolve_token
from .sse import SseParser

logger = logging.getLogger("foundgine")

DEFAULT_PROTOCOL_VERSION = "2025-06-18"
SESSION_HEADER = "Mcp-Session-Id"
PROTOCOL_HEADER = "MCP-Protocol-Version"

# Notification streams stay open (heartbeats keep them alive), so they get no read timeout.
_STREAM_TIMEOUT = httpx.Timeout(10.0, read=None)


class McpSession:
    """MCP HTTP session. Exposed as ``client.mcp`` for protocol-level operations.

    Initialisation is lazy and happens exactly once, even for concurrent first calls.
    """

    def __init__(
        self,
        base_url: str,
        http_client: httpx.AsyncClient,
        *,
        path: str = "/mcp",
        protocol_version: str = DEFAULT_PROTOCOL_VERSION,
        token: TokenProvider = None,
        user_agent: str = USER_AGENT,
    ) -> None:
        self.url = join_url(base_url, path)
        self._http = http_client
        self._token = token
        self._requested_version = protocol_version
        self._user_agent = user_agent
        self._version: str | None = None
        self._session: str | None = None
        self._ready = False
        self._init_lock = asyncio.Lock()
        self._next_id = 1
        self._last_event_id: str | None = None

    @property
    def session(self) -> str | None:
        """The current ``Mcp-Session-Id`` (None before initialisation, after close, or after expiry)."""
        return self._session

    @property
    def last_event_id(self) -> str | None:
        return self._last_event_id

    async def initialize(self) -> None:
        if self._ready:
            return
        async with self._init_lock:
            if not self._ready:
                await self._initialize_core()

    async def request(
        self, method: str, params: dict[str, Any] | None = None, *, cancel: asyncio.Event | None = None
    ) -> Any:
        """Send one JSON-RPC request and return its ``result``.

        ``cancel`` mirrors the TypeScript ``AbortSignal``: when set, the call raises
        :class:`FoundgineAbortError` and a best-effort ``notifications/cancelled`` is sent.
        Cancelling the awaiting task raises ``CancelledError`` and sends the same notification.
        """
        if cancel is not None and cancel.is_set():
            raise FoundgineAbortError()
        await self.initialize()
        request_id = self._next_id
        self._next_id += 1
        message: dict[str, Any] = {"jsonrpc": "2.0", "id": request_id, "method": method}
        if params is not None:
            message["params"] = params
        try:
            response = await await_or_cancel(self._post(message), cancel)
        except FoundgineAbortError:
            await self._best_effort_cancel(request_id)
            raise
        except asyncio.CancelledError:
            # Shielded so the notification still goes out while the caller's cancellation propagates.
            with contextlib.suppress(BaseException):
                await asyncio.shield(self._best_effort_cancel(request_id))
            raise
        if not response.is_success:
            error = await error_for(response)
            if response.status_code == 404 and self._session:
                self._expire(self._session)
                raise FoundgineSessionExpiredError(error.message, error.body)
            raise error
        if response.status_code in (202, 204):
            return None
        content_type = response.headers.get("content-type", "").split(";")[0].strip().lower()
        wire: Any
        try:
            if content_type == "text/event-stream":
                events = SseParser().push(response.content)
                first = next((e for e in events if e.data), None)
                wire = json.loads(first.data) if first else None
            else:
                wire = response.json()
        except ValueError as exc:
            raise FoundgineError("The server answered with a body that is not JSON") from exc
        if not isinstance(wire, dict):
            raise FoundgineError("The server answered with a JSON-RPC message that is not an object")
        rpc_error = wire.get("error")
        if isinstance(rpc_error, dict):
            code = rpc_error.get("code")
            text = rpc_error.get("message")
            raise FoundgineRpcError(
                code if isinstance(code, int) and not isinstance(code, bool) else -32603,
                text if isinstance(text, str) else "",
                rpc_error.get("data"),
            )
        if wire.get("id") != request_id:
            raise FoundgineError("The server returned a JSON-RPC response with an unexpected id")
        return wire.get("result")

    async def notify(self, method: str, params: dict[str, Any] | None = None) -> None:
        await self.initialize()
        message: dict[str, Any] = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            message["params"] = params
        response = await self._post(message)
        if not response.is_success:
            raise await error_for(response)

    async def notifications(
        self,
        *,
        cancel: asyncio.Event | None = None,
        last_event_id: str | None = None,
        reconnect_delay: float = 1.0,
    ) -> AsyncGenerator[dict[str, Any], None]:
        """Yield server notifications, resuming with ``Last-Event-ID`` after the server closes the stream.

        Cancelling (``cancel`` event) ends the iteration cleanly. If you stop iterating early, close the
        iterator (``contextlib.aclosing``) so the server's single stream slot is released.
        """
        if last_event_id is not None:
            self._last_event_id = last_event_id
        while cancel is None or not cancel.is_set():
            await self.initialize()
            headers = await self._headers(self._session, False, "text/event-stream")
            if self._last_event_id is not None:
                headers["Last-Event-ID"] = self._last_event_id
            request = self._http.build_request("GET", self.url, headers=headers, timeout=_STREAM_TIMEOUT)
            try:
                response = await await_or_cancel(self._http.send(request, stream=True), cancel)
            except FoundgineAbortError:
                return
            except httpx.TransportError as exc:
                raise FoundgineError("The notification stream could not be opened") from exc
            try:
                if not response.is_success:
                    error = await error_for(response)
                    if response.status_code == 404:
                        self._expire(self._session)
                        raise FoundgineSessionExpiredError(error.message, error.body)
                    if response.status_code == 410:
                        raise FoundgineResyncRequiredError(error.message, error.body)
                    raise error
                parser = SseParser()
                chunks = cast("AsyncGenerator[bytes, None]", response.aiter_bytes())
                try:
                    while cancel is None or not cancel.is_set():
                        try:
                            chunk = await await_or_cancel(chunks.__anext__(), cancel)
                        except StopAsyncIteration:
                            break
                        except FoundgineAbortError:
                            return
                        except httpx.TransportError:
                            break  # established stream dropped; reconnect from the last consumed event
                        for event in parser.push(chunk):
                            try:
                                message = json.loads(event.data)
                            except ValueError:
                                continue
                            if event.id is not None:
                                self._last_event_id = event.id
                            if isinstance(message, dict):
                                yield message
                finally:
                    with contextlib.suppress(Exception):
                        await chunks.aclose()
            finally:
                with contextlib.suppress(Exception):
                    await asyncio.shield(response.aclose())
            if cancel is not None and cancel.is_set():
                return
            try:
                if cancel is None:
                    await asyncio.sleep(reconnect_delay)
                else:
                    await asyncio.wait_for(cancel.wait(), reconnect_delay)
                    return
            except TimeoutError:
                pass

    async def close(self) -> None:
        """End the session (best effort, idempotent)."""
        session, self._session, self._ready = self._session, None, False
        if not session:
            return
        try:
            headers = await self._headers(session, False)
            response = await self._http.delete(self.url, headers=headers)
            if not response.is_success:
                raise await error_for(response)
        except Exception:
            logger.debug("MCP session teardown failed (ignored)", exc_info=True)

    async def notify_initialized(self) -> None:
        response = await self._post({"jsonrpc": "2.0", "method": "notifications/initialized"})
        if not response.is_success:
            raise await error_for(response)

    # -- internals ---------------------------------------------------------------------------------------

    async def _initialize_core(self) -> None:
        request_id = self._next_id
        self._next_id += 1
        payload = {
            "jsonrpc": "2.0",
            "id": request_id,
            "method": "initialize",
            "params": {
                "protocolVersion": self._requested_version,
                "capabilities": {},
                "clientInfo": {"name": "foundgine-python", "version": "3.3.4"},
            },
        }
        try:
            response = await self._http.post(
                self.url, headers=await self._headers(None, True), content=json.dumps(payload)
            )
        except httpx.TransportError as exc:
            raise FoundgineError("MCP initialization failed before an HTTP response was received") from exc
        if not response.is_success:
            raise await error_for(response)
        try:
            wire = response.json()
        except ValueError as exc:
            raise FoundgineError("The server answered the initialize request with a body that is not JSON") from exc
        if not isinstance(wire, dict) or wire.get("id") != request_id:
            raise FoundgineError("The server returned an unexpected initialize response id")
        result = wire.get("result")
        version = result.get("protocolVersion") if isinstance(result, dict) else None
        self._version = version if isinstance(version, str) else self._requested_version
        self._session = response.headers.get(SESSION_HEADER)
        self._ready = True
        await self.notify_initialized()

    async def _post(self, message: dict[str, Any]) -> httpx.Response:
        headers = await self._headers(self._session, True)
        try:
            return await self._http.post(self.url, headers=headers, content=json.dumps(message))
        except httpx.TransportError as exc:
            raise FoundgineError("The MCP request failed before an HTTP response was received") from exc

    async def _headers(
        self, session: str | None, is_json: bool, accept: str = "application/json, text/event-stream"
    ) -> dict[str, str]:
        headers = {"Accept": accept, "User-Agent": self._user_agent}
        token = await resolve_token(self._token)
        if token:
            headers["Authorization"] = f"Bearer {token}"
        if session:
            headers[SESSION_HEADER] = session
        if self._ready and self._version:
            headers[PROTOCOL_HEADER] = self._version
        if is_json:
            headers["Content-Type"] = "application/json"
        return headers

    def _expire(self, session: str | None) -> None:
        if self._session == session:
            self._session = None
            self._ready = False

    async def _best_effort_cancel(self, request_id: int) -> None:
        try:
            await self.notify("notifications/cancelled", {"requestId": request_id, "reason": "cancelled by client"})
        except Exception:
            logger.debug("notifications/cancelled failed (ignored)", exc_info=True)
