"""Private HTTP helpers shared by the MCP and workflow clients."""

from __future__ import annotations

import inspect
import json
from collections.abc import Awaitable, Callable
from typing import Any

import httpx

from .errors import FoundgineHttpError

TokenProvider = str | Callable[[], str | Awaitable[str | None] | None] | None
DEFAULT_TIMEOUT = httpx.Timeout(connect=10.0, read=60.0, write=60.0, pool=10.0)
STREAM_TIMEOUT = httpx.Timeout(connect=10.0, read=None, write=60.0, pool=10.0)


async def resolve_token(token: TokenProvider) -> str | None:
    """Resolve a fixed, synchronous, or asynchronous bearer-token provider."""
    value: Any = token() if callable(token) else token
    if inspect.isawaitable(value):
        value = await value
    return value if isinstance(value, str) and value else None


def join_url(base_url: str, path: str) -> str:
    """Join a base URL and absolute-or-relative path without duplicate slashes."""
    return base_url.rstrip("/") + "/" + path.lstrip("/")


def error_for(status: int, body: bytes, secret: str | None = None) -> FoundgineHttpError:
    """Create a safe HTTP error; request headers and credentials are never included."""
    parsed: Any = None
    detail: str | None = None
    try:
        parsed = json.loads(body.decode("utf-8", errors="replace")) if body else None
    except ValueError:
        pass
    if isinstance(parsed, dict):
        value = parsed.get("error")
        if isinstance(value, str):
            detail = value
        elif isinstance(value, dict) and isinstance(value.get("message"), str):
            detail = value["message"]
    if detail is None and body and parsed is None:
        detail = body.decode("utf-8", errors="replace")[:200].strip() or None
    if detail and secret:
        detail = detail.replace(secret, "[redacted]")
    return FoundgineHttpError(status, f"HTTP {status}: {detail}" if detail else f"HTTP {status}", parsed)
