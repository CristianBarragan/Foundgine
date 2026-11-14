"""Shared test helpers: a recording httpx.MockTransport (the Python analogue of the TS ``mockFetch``)."""

from __future__ import annotations

import json
from collections.abc import Awaitable, Callable
from dataclasses import dataclass
from typing import Any

import httpx

BASE = "https://foundgine.test"


@dataclass
class Call:
    url: str
    method: str
    headers: httpx.Headers
    body: Any

    @property
    def rpc(self) -> str | None:
        return self.body.get("method") if isinstance(self.body, dict) else None

    @property
    def rpc_id(self) -> Any:
        return self.body.get("id") if isinstance(self.body, dict) else None


Handler = Callable[[Call], "httpx.Response | Awaitable[httpx.Response]"]


def mock_http(handler: Handler) -> tuple[httpx.AsyncClient, list[Call]]:
    calls: list[Call] = []

    async def transport(request: httpx.Request) -> httpx.Response:
        content = request.content
        call = Call(str(request.url), request.method, request.headers, json.loads(content) if content else None)
        calls.append(call)
        result = handler(call)
        return await result if isinstance(result, Awaitable) else result

    return httpx.AsyncClient(transport=httpx.MockTransport(transport)), calls


def json_response(data: Any, status: int = 200, headers: dict[str, str] | None = None) -> httpx.Response:
    return httpx.Response(status, json=data, headers=headers)


def accepted() -> httpx.Response:
    return httpx.Response(202)


def rpc_ok(call: Call, result: Any, headers: dict[str, str] | None = None) -> httpx.Response:
    return json_response({"jsonrpc": "2.0", "id": call.rpc_id, "result": result}, headers=headers)
