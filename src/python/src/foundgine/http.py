"""HTTP helpers shared by the MCP and workflow clients. Mirrors ``http.ts``."""

from __future__ import annotations

import asyncio
import inspect
import json
from collections.abc import Awaitable, Callable
from typing import Any, TypeVar

import httpx

from .errors import FoundgineAbortError, FoundgineHttpError

T = TypeVar("T")

TokenProvider = str | Callable[[], "Awaitable[str | None] | str | None"] | None
"""A bearer token: a string, or a sync/async callable resolved for every request."""

USER_AGENT = "foundgine-python/3.3.4"


async def resolve_token(token: TokenProvider) -> str | None:
    """Resolve a token provider. Empty or non-string values mean "no token"."""
    value: Any = token
    if callable(value):
        value = value()
        if inspect.isawaitable(value):
            value = await value
    return value if isinstance(value, str) and value else None


def join_url(base_url: str, path: str) -> str:
    return f"{base_url.rstrip('/')}/{path.lstrip('/')}"


async def error_for(response: httpx.Response) -> FoundgineHttpError:
    """Build an HTTP error from a non-success response (reading the body if it is still streaming)."""
    await response.aread()
    text = response.text
    body: Any = None
    try:
        body = json.loads(text) if text else None
    except ValueError:
        body = None
    detail: str | None = None
    if isinstance(body, dict):
        error = body.get("error")
        if isinstance(error, str):
            detail = error
        elif isinstance(error, dict) and isinstance(error.get("message"), str):
            detail = error["message"]
    if not detail and text and body is None:
        detail = text[:200].strip() or None
    status = response.status_code
    return FoundgineHttpError(status, f"HTTP {status}: {detail}" if detail else f"HTTP {status}", body)


async def ensure_success(response: httpx.Response) -> None:
    if not response.is_success:
        raise await error_for(response)


async def await_or_cancel(awaitable: Awaitable[T], cancel: asyncio.Event | None) -> T:
    """Await ``awaitable``; if ``cancel`` is set first, cancel it and raise :class:`FoundgineAbortError`.

    Cancelling the *calling* task cancels the inner work too and ``CancelledError`` propagates.
    """
    if cancel is None:
        return await awaitable
    if cancel.is_set():
        if inspect.iscoroutine(awaitable):
            awaitable.close()
        raise FoundgineAbortError()
    work = asyncio.ensure_future(awaitable)
    waiter = asyncio.ensure_future(cancel.wait())
    try:
        await asyncio.wait({work, waiter}, return_when=asyncio.FIRST_COMPLETED)
        if work.done():
            return work.result()
        work.cancel()
        await asyncio.gather(work, return_exceptions=True)
        raise FoundgineAbortError()
    finally:
        waiter.cancel()
        if not work.done():
            work.cancel()
