"""Real-socket tests: a tiny HTTP/1.1 server on 127.0.0.1, because MockTransport cannot model a live stream."""

from __future__ import annotations

import asyncio
import contextlib
import json
import threading
from collections.abc import AsyncIterator
from typing import Any

import pytest

from foundgine import FoundgineClient


class Server:
    def __init__(self) -> None:
        self.requests: list[tuple[str, dict[str, str], bytes]] = []
        self.stream_closed = threading.Event()  # thread-safe: the server may run on another loop thread
        self.streams = 0

    async def handle(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        is_stream = False
        try:
            line = (await reader.readline()).decode().strip()
            headers: dict[str, str] = {}
            while (h := (await reader.readline()).decode().strip()) != "":
                k, _, v = h.partition(":")
                headers[k.lower()] = v.strip()
            body = await reader.readexactly(int(headers.get("content-length", "0")))
            self.requests.append((line, headers, body))
            if line.startswith("POST"):
                msg: dict[str, Any] = json.loads(body)
                if "id" not in msg:
                    writer.write(b"HTTP/1.1 202 Accepted\r\nConnection: close\r\nContent-Length: 0\r\n\r\n")
                else:
                    out = json.dumps({"jsonrpc": "2.0", "id": msg["id"], "result": {"tools": []}}).encode()
                    extra = b"Mcp-Session-Id: live-1\r\n" if msg["method"] == "initialize" else b""
                    writer.write(
                        b"HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: application/json\r\n"
                        + extra
                        + f"Content-Length: {len(out)}\r\n\r\n".encode()
                        + out
                    )
            elif line.startswith("GET"):
                self.streams += 1
                is_stream = True
                writer.write(
                    b"HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: text/event-stream\r\n"
                    b"Transfer-Encoding: chunked\r\n\r\n"
                )
                n = self.streams
                frame = f'id: {n}\ndata: {{"method":"n{n}"}}\n\n'.encode()
                writer.write(f"{len(frame):x}\r\n".encode() + frame + b"\r\n")
                await writer.drain()
                if n == 2:  # second stream stays open until the client goes away
                    await reader.read()  # returns at EOF, i.e. when the client closes the connection
                else:
                    writer.write(b"0\r\n\r\n")
            elif line.startswith("DELETE"):
                writer.write(b"HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n")
            await writer.drain()
        except (ConnectionError, asyncio.IncompleteReadError):
            pass
        finally:
            if is_stream:
                self.stream_closed.set()
            writer.close()


@pytest.fixture
async def server() -> AsyncIterator[tuple[Server, str]]:
    state = Server()
    srv = await asyncio.start_server(state.handle, "127.0.0.1", 0)
    port = srv.sockets[0].getsockname()[1]
    yield state, f"http://127.0.0.1:{port}"
    srv.close()
    with contextlib.suppress(Exception):
        await srv.wait_closed()


async def test_real_http_roundtrip_and_stream_resume(server: tuple[Server, str]) -> None:
    state, url = server
    async with FoundgineClient(url, "tok") as client:
        assert await client.list_capabilities() == []
        cancel = asyncio.Event()
        seen: list[str] = []
        async for note in client.notifications(cancel=cancel, reconnect_delay=0):
            seen.append(note["method"])
            if len(seen) == 2:
                cancel.set()
    assert seen == ["n1", "n2"]
    gets = [r for r in state.requests if r[0].startswith("GET")]
    assert "last-event-id" not in gets[0][1] and gets[1][1]["last-event-id"] == "1"
    assert all(r[1]["authorization"] == "Bearer tok" for r in state.requests)
    assert any(r[0].startswith("DELETE") for r in state.requests)


async def test_cancel_while_the_stream_is_idle_ends_cleanly_and_frees_the_socket(server: tuple[Server, str]) -> None:
    state, url = server
    state.streams = 1  # next GET is the long-lived "n2" stream
    client = FoundgineClient(url)
    cancel = asyncio.Event()
    got: list[str] = []

    async def consume() -> None:
        async for note in client.notifications(cancel=cancel):
            got.append(note["method"])

    task = asyncio.ensure_future(consume())
    for _ in range(100):
        if got:
            break
        await asyncio.sleep(0.01)
    cancel.set()
    await asyncio.wait_for(task, 2)
    assert await asyncio.to_thread(state.stream_closed.wait, 2)
    assert got == ["n2"]
    await client.close()
