# foundgine-sdk (Python)

A thin Python client for Foundgine MCP and Temporal workflow HTTP transports.

> Part of the Foundgine repository. This client talks to a running Foundgine server (the .NET or Java implementation hosting the MCP and Temporal endpoints); it does not contain the semantic runtime. See the [client SDK overview](../../docs/wiki/client-sdks.md) and the [source layout](../README.md).

Requires Python 3.11+ and `httpx`.

## Local install and checks

Run from `src/python/`:

```sh
pip install -e ".[dev]"
ruff check . && mypy && pytest
```

The project metadata names the distribution `foundgine-sdk`. This repository snapshot documents the editable source install; it does not claim that a PyPI release is available.

## MCP client

```python
import asyncio
import json
from foundgine import FoundgineClient


async def main() -> None:
    async with FoundgineClient("https://example.invalid", token=lambda: "tok") as client:
        tools = await client.list_capabilities()
        # Query tools take one `intentJson` string; names come from foundgine_capabilities.
        intent = {"rootEntity": "Customer", "selections": [{"field": "Name"}], "limit": 20}
        result = await client.execute("foundgine_query_semantic", {"intentJson": json.dumps(intent)})
        print([t.name for t in tools], result.content, result.is_error)


asyncio.run(main())
```

`token` may be a string or a sync/async callable; it is resolved for every request. Redirects are not followed. If you pass `http_client=`, the SDK never closes it. Without one, the SDK creates its own client with explicit timeouts (connect 10 s, read 60 s; `DEFAULT_TIMEOUT`) because httpx's 5 s default would break slow tool calls.

### Synchronous client (scripts and notebooks)

```python
from foundgine import FoundgineSyncClient

with FoundgineSyncClient("https://example.invalid", token="tok") as client:
    print([t.name for t in client.list_capabilities()])
    print(client.workflows.wait_for_completion("order-approval", "order-1042", timeout=60).temporal_status)
```

Same methods and exceptions as the async client, run on a private event-loop thread (so it also works inside Jupyter). `watch` and the `cancel` event are async-only; Ctrl-C cancels the in-flight call. `notifications()` is a blocking generator: wrap it in `contextlib.closing` if you stop early.

## Notifications

```python
import asyncio
import contextlib

cancel = asyncio.Event()
async with contextlib.aclosing(client.notifications(cancel=cancel)) as stream:
    async for message in stream:
        print(message)
```

The stream resumes using `Last-Event-ID` after the server closes it. Setting `cancel` ends the iteration cleanly. If you stop early, close the iterator (`contextlib.aclosing`, as above) so the server's single stream slot is released. There is no read timeout on the stream (heartbeats keep it alive).

## Temporal workflows

The workflow client follows the HTTP routes exposed by Foundgine Temporal:

```python
started, watch = await client.workflows.start_and_watch(
    "order-approval", "order-1042", {"orderId": "order-1042"}, interval=1.0, timeout=120.0
)
print(started.workflow_id)
async for status in watch.updates():
    print(status.temporal_status)
final = await watch.done
```

The catalog can also be inspected before starting work:

```python
names = await client.workflows.processes()
definition = await client.workflows.describe(names[0])
print([(step.name, step.requires_approval) for step in definition.steps])
```

Also available: `processes`, `describe`, `start`, `status`, `decide`, `cancel`, `wait_for_completion`, and `watch`. Catalog descriptions expose safe step metadata only; internal operation identifiers are not returned by the server. `watch()` returns immediately and must be called inside a running event loop. `updates()` replays distinct observed states to late consumers. `stop()` aborts polling (`done` and `updates()` then raise `FoundgineAbortError`); `async with watch:` stops it on exit. `on_update` and `on_error` may be plain functions or coroutine functions, and their failures never kill the poll loop. Process and business-key path segments are percent-encoded. `decide` has no caller-identity argument: the server stamps the decision subject from its trusted security context.

## Deliberate differences from `@foundgine/typescript`

| TypeScript | Python | Why |
|---|---|---|
| `camelCase`, `intervalMs`, `timeoutMs`, `notFoundGraceMs`, `reconnectDelayMs` | `snake_case`, `interval`, `timeout`, `not_found_grace`, `reconnect_delay` in **seconds** | Python convention |
| `AbortSignal` (`signal`) | optional `asyncio.Event` named `cancel`; task cancellation also works and sends `notifications/cancelled` | no `AbortSignal` in Python |
| `new FoundgineClient({ baseUrl })` | `FoundgineClient(base_url)`, usable as `async with` | idiom |
| `watch()` object literal | `WorkflowWatch` class (`done` is an `asyncio.Task`) | idiom |
| `watch.stop()` after finishing makes `updates()` throw | `stop()` is a no-op once finished | avoids a completed watch turning into an error |
| abort during a status poll is counted as a poll error and reaches `onError` | `FoundgineAbortError` is re-raised immediately | an abort is not a failure |
| workflow transport failures surface as raw `fetch` errors | wrapped in `FoundgineError` (as MCP calls already are) | consistent error surface |
| 400-without-session CORS hint | not applicable | no browser CORS in Python |

## Status and known limits

Implemented and tested: typed errors, SSE parser (including shared conformance vectors), MCP session initialization (once, even under concurrency), JSON-RPC requests, paged `list_capabilities`, `execute`, cancellation, resumable notification streaming, Temporal start/status/decision/cancel routes, `wait_for_completion`, `watch`, `start_and_watch`, and the workflow catalog (`processes()`, `describe()`). As in `@foundgine/typescript`, `watch` retries other polling errors up to `max_consecutive_errors`, but a 401 is fatal at once.

Not yet done or verified: an integration test against a real Foundgine server. Developed and tested on Python 3.12; CI covers 3.11–3.14. `tests/test_conformance.py` runs the shared vectors in `src/conformance`; they skip locally when absent and fail when `CI` is set. This is a development package, not a release-ready SDK.