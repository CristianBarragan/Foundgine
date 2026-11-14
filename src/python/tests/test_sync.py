"""The synchronous facade, against the real loopback server (it needs a live socket, not MockTransport)."""

from __future__ import annotations

import asyncio
import contextlib
import threading
from collections.abc import Iterator
from typing import Any

import pytest
from test_loopback import Server

from foundgine import FoundgineHttpError, FoundgineSyncClient


@pytest.fixture
def live() -> Iterator[tuple[Server, str]]:
    """Run the loopback server on its own loop thread so blocking calls from the test thread can reach it."""
    loop = asyncio.new_event_loop()
    state: dict[str, Any] = {}
    ready = threading.Event()

    async def main() -> None:
        server = Server()
        srv = await asyncio.start_server(server.handle, "127.0.0.1", 0)
        state.update(server=server, url=f"http://127.0.0.1:{srv.sockets[0].getsockname()[1]}", stop=asyncio.Event())
        ready.set()
        await state["stop"].wait()
        srv.close()
        await srv.wait_closed()

    thread = threading.Thread(target=lambda: loop.run_until_complete(main()), daemon=True)
    thread.start()
    assert ready.wait(5)
    yield state["server"], state["url"]
    loop.call_soon_threadsafe(state["stop"].set)
    thread.join(5)
    loop.close()


def test_blocking_calls_and_context_manager(live: tuple[Server, str]) -> None:
    server, url = live
    with FoundgineSyncClient(url, token=lambda: "tok") as client:
        assert client.list_capabilities() == []
        result = client.execute("anything")  # the loopback server answers every request with {"tools": []}
        assert result.content == [] and result.is_error is False
    assert any(r[0].startswith("DELETE") for r in server.requests)
    assert all(r[1]["authorization"] == "Bearer tok" for r in server.requests)
    with pytest.raises(RuntimeError, match="closed"):
        client.list_capabilities()
    client.close()  # idempotent


def test_errors_are_the_same_exception_types() -> None:
    with FoundgineSyncClient("http://127.0.0.1:9") as client, pytest.raises(Exception) as info:
        client.list_capabilities()
    assert not isinstance(info.value, FoundgineHttpError)  # connection refused: FoundgineError, not an HTTP status


def test_blocking_notification_iterator_and_early_close(live: tuple[Server, str]) -> None:
    server, url = live
    server.streams = 1  # the next GET is the long-lived stream that sends one event and stays open
    with FoundgineSyncClient(url) as client, contextlib.closing(client.notifications()) as stream:
        assert next(stream) == {"method": "n2"}
    assert server.stream_closed.wait(2)


def test_it_works_when_called_from_inside_a_running_loop(live: tuple[Server, str]) -> None:
    _, url = live

    async def inside() -> int:
        with FoundgineSyncClient(url) as client:  # blocks the loop briefly, but must not deadlock or raise
            return len(client.list_capabilities())

    assert asyncio.run(inside()) == 0
