"""High-level client. Mirrors ``client.ts``."""

from __future__ import annotations

import asyncio
import re
from collections.abc import AsyncGenerator
from dataclasses import dataclass, field
from types import TracebackType
from typing import Any

import httpx

from .errors import FoundgineError
from .http import TokenProvider
from .mcp import DEFAULT_PROTOCOL_VERSION, McpSession
from .workflows import WorkflowClient

DEFAULT_TIMEOUT = httpx.Timeout(60.0, connect=10.0)
"""httpx's own 5 s default would break slow tool calls, so the SDK sets explicit timeouts."""


@dataclass(frozen=True, slots=True)
class Tool:
    """One MCP tool description from ``tools/list``."""

    name: str
    description: str | None = None
    input_schema: dict[str, Any] = field(default_factory=dict)
    raw: dict[str, Any] = field(default_factory=dict)


@dataclass(frozen=True, slots=True)
class ToolResult:
    """Result of ``tools/call``. A failing tool is ``is_error=True`` data, not an exception."""

    content: list[dict[str, Any]]
    is_error: bool
    structured_content: Any = None
    raw: dict[str, Any] = field(default_factory=dict)


class FoundgineClient:
    """Thin client for Foundgine's MCP and workflow HTTP surfaces.

    ``token`` may be a string or a sync/async callable, resolved for every request. If ``http_client`` is
    supplied the caller owns it and :meth:`close` leaves it open. Redirects are not followed.
    """

    def __init__(
        self,
        base_url: str,
        token: TokenProvider = None,
        *,
        http_client: httpx.AsyncClient | None = None,
        mcp_path: str | None = None,
        protocol_version: str | None = None,
        timeout: httpx.Timeout | float | None = None,
    ) -> None:
        if not re.match(r"^https?://", base_url, re.IGNORECASE):
            raise ValueError("base_url must start with http:// or https://")
        self.base_url = base_url.rstrip("/")
        self._owns_http = http_client is None
        self._http = http_client or httpx.AsyncClient(
            timeout=DEFAULT_TIMEOUT if timeout is None else timeout, follow_redirects=False
        )
        self._closed = False
        self.mcp = McpSession(
            self.base_url,
            self._http,
            path=mcp_path or "/mcp",
            protocol_version=protocol_version or DEFAULT_PROTOCOL_VERSION,
            token=token,
        )
        self.workflows = WorkflowClient(self.base_url, self._http, token)

    async def list_capabilities(self) -> list[Tool]:
        """List every tool, following ``nextCursor`` until exhausted."""
        tools: list[Tool] = []
        cursor: str | None = None
        while True:
            result = await self.mcp.request("tools/list", {"cursor": cursor} if cursor else {})
            if not isinstance(result, dict):
                raise FoundgineError("tools/list returned no result object")
            items = result.get("tools")
            if isinstance(items, list):
                for item in items:
                    if not isinstance(item, dict) or not isinstance(item.get("name"), str):
                        continue
                    schema = item.get("inputSchema")
                    description = item.get("description")
                    tools.append(
                        Tool(
                            name=item["name"],
                            description=description if isinstance(description, str) else None,
                            input_schema=schema if isinstance(schema, dict) else {},
                            raw=item,
                        )
                    )
            next_cursor = result.get("nextCursor")
            cursor = next_cursor if isinstance(next_cursor, str) and next_cursor else None
            if cursor is None:
                return tools

    async def execute(
        self, name: str, arguments: dict[str, Any] | None = None, *, cancel: asyncio.Event | None = None
    ) -> ToolResult:
        """Call a tool. Protocol failures raise :class:`FoundgineRpcError`; tool failures set ``is_error``."""
        result = await self.mcp.request("tools/call", {"name": name, "arguments": arguments or {}}, cancel=cancel)
        if not isinstance(result, dict):
            raise FoundgineError("tools/call returned no result object")
        content = result.get("content")
        return ToolResult(
            content=[c for c in content if isinstance(c, dict)] if isinstance(content, list) else [],
            is_error=result.get("isError") is True,
            structured_content=result.get("structuredContent"),
            raw=result,
        )

    def notifications(
        self,
        *,
        cancel: asyncio.Event | None = None,
        last_event_id: str | None = None,
        reconnect_delay: float = 1.0,
    ) -> AsyncGenerator[dict[str, Any], None]:
        """Resumable notification stream. See :meth:`McpSession.notifications`."""
        return self.mcp.notifications(cancel=cancel, last_event_id=last_event_id, reconnect_delay=reconnect_delay)

    async def close(self) -> None:
        """End the MCP session and close the HTTP client if the SDK created it. Idempotent."""
        if self._closed:
            return
        self._closed = True
        await self.mcp.close()
        if self._owns_http:
            await self._http.aclose()

    async def __aenter__(self) -> FoundgineClient:
        return self

    async def __aexit__(
        self, exc_type: type[BaseException] | None, exc: BaseException | None, tb: TracebackType | None
    ) -> None:
        await self.close()
