"""Exception types. They mirror the TypeScript SDK's error classes (REQUIREMENTS.md, PY-ERR-1)."""

from __future__ import annotations

from typing import Any


class FoundgineError(Exception):
    """Base class for every error this SDK raises on purpose."""


class FoundgineHttpError(FoundgineError):
    """The server answered with an error status. ``status`` is the HTTP status code."""

    def __init__(self, status: int, message: str | None = None, body: Any = None) -> None:
        self.status = status
        self.body = body
        self.message = message or f"HTTP {status}"
        super().__init__(self.message)


class FoundgineSessionExpiredError(FoundgineHttpError):
    """HTTP 404 on an MCP session call: the session is gone. The next call starts a new one."""

    def __init__(self, message: str | None = None, body: Any = None) -> None:
        super().__init__(404, message or "MCP session expired or unknown", body)


class FoundgineResyncRequiredError(FoundgineHttpError):
    """HTTP 410 on the notification stream: the requested event id is no longer buffered. Re-read state, then resume."""

    def __init__(self, message: str | None = None, body: Any = None) -> None:
        super().__init__(
            410, message or "Event replay buffer no longer holds the requested event; resync required", body
        )


class FoundgineRpcError(FoundgineError):
    """A JSON-RPC ``error`` object (a protocol failure; a failing tool is data, not this)."""

    def __init__(self, code: int, message: str, data: Any = None) -> None:
        self.code = code
        self.data = data
        self.message = message
        super().__init__(f"JSON-RPC error {code}: {message}")


class FoundgineAbortError(FoundgineError):
    """The operation was stopped by the caller (cancel event or ``Watch.stop()``)."""

    def __init__(self, message: str = "Operation aborted") -> None:
        super().__init__(message)


class FoundgineWatchTimeoutError(FoundgineError):
    """``Watch.done`` ran out of time. ``last_status`` is the last status seen, or None."""

    def __init__(self, last_status: Any = None, message: str = "Timed out waiting for the workflow to finish") -> None:
        self.last_status = last_status
        super().__init__(message)
