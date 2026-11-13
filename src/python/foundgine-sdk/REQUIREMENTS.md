# Foundgine Python SDK: requirements

Status: draft for review, 2026-11-14. Target: a Python client that mirrors `src/typescript/foundgine-sdk` (`@foundgine/sdk`).

> **Update 2026-11-14 (Step 0 done, Steps 1-3 implemented).** The TS source has now been read. Where this document
> was inferred from tests, the TS source wins. Settled points:
> - **Workflow `processes()` / `describe()` DO exist in the TS SDK** (`WorkflowProcessDescription`,
>   `WorkflowStepDescription`) and are implemented here (also on the sync client). An earlier note claiming otherwise was stale.
>   `WorkflowStatus` is `(workflow_id, temporal_status, process, raw)` and `process` is passed through untouched.
> - `wait_for_completion` **raises** `FoundgineWatchTimeoutError(last_status)` on timeout (it does not return the last
>   status); `watch().done` does the same. PY-WF-4 is corrected accordingly.
> - "Finished" = `temporal_status.upper()` in `COMPLETED, FAILED, CANCELED, CANCELLED, TERMINATED, TIMED_OUT`; anything
>   else (including unknown values) is not terminal. There is no result-based rule, so the `ContinuedAsNew` loop concern
>   does not arise in the SDK (a `timeout` still bounds it).
> - `decide` omits `reason` when it is not given. Poll jitter: none. Callback failures are swallowed (logged at DEBUG).
> - `FoundgineAbortError` and `FoundgineWatchTimeoutError` extend `FoundgineError`. A 401 during `watch` is fatal at once
>   (TS `workflows.ts` rethrows it); implemented the same way.
> - TS has no request timeouts and no `Mcp-Session-Id` reuse across clients; PY-NFR-3 timeouts are Python-only.
> Remaining open: sync facade (PY-API-9), CI/release (section 8), integration server (PY-TEST-6), questions 1, 2, 6, 8.

**Basis and its limit.** The TypeScript SDK source (`src/`) was not available when this was written. Everything below about
the TS behaviour is derived from its tests (`sdk.test.ts`, `conformance.test.ts`, `mock-server.ts`, `browser-check.mjs`), its
`package.json`, and the shared server contract in `src/conformance`. Anything marked **[confirm in TS source]** is an
inference. Task 0 of the plan is to read `src/` and settle those points before writing code.

Priorities: **M** must (blocks 0.1.0), **S** should (0.1.0 if cheap, otherwise 0.2), **C** could.

## 1. Goal and scope

A Python SDK that speaks the same wire protocol as the TS SDK, with the same capabilities and the same failure behaviour,
written the way Python developers expect (asyncio, snake_case, exceptions, type hints).

In scope: transport only, as for the TS SDK ("Thin MCP client for Foundgine (transport only)").
- MCP over HTTP: `initialize`, `tools/list` (paged), `tools/call`, `notifications/cancelled`, session teardown (`DELETE`).
- Server notification stream (SSE) with replay and resume (`Last-Event-ID`).
- Temporal workflow HTTP routes: list/describe processes, start, status, decision, cancel.
- Polling helpers: `wait_for_completion`, `watch`, `start_and_watch`.
- Typed errors that match the TS error classes.

Out of scope (non-goals):
- Server-side code, or hosting an MCP server.
- Business logic of any kind (the TS SDK has none either).
- trio or other non-asyncio runtimes (httpx would allow it; we do not test or promise it).
- Browser/Pyodide support.
- Code generation from a schema (none exists today).

## 2. Users and use cases

1. Backend services and agents calling Foundgine tools (`execute`) and starting or watching approval workflows.
2. Scripts and notebooks (these want a sync API; see PY-API-9).
3. Test harnesses that drive the Foundgine server from Python.
4. Operators approving or cancelling workflows from a CLI or chat bot (`decide`, `cancel`).

## 3. Public API (parity target)

Names are snake_case equivalents of the TS names. Wire JSON stays camelCase; the SDK maps it at the boundary.

| TS | Python |
|---|---|
| `new FoundgineClient({ baseUrl, token, fetch })` | `FoundgineClient(base_url, token=None, *, http_client=None, ...)` |
| `client.listCapabilities()` | `await client.list_capabilities()` |
| `client.execute(name, args, { signal })` | `await client.execute(name, arguments=None, *, cancel=None)` |
| `client.notifications({ signal, lastEventId, reconnectDelayMs })` | `client.notifications(*, last_event_id=None, reconnect_delay=..., cancel=None)` (async iterator) |
| `client.mcp.request(method, params)`, `.initialize()`, `.session`, `.notifications()` | `client.mcp.request(...)`, `.initialize()`, `.session`, `.notifications()` |
| `client.close()` | `await client.close()` and `async with FoundgineClient(...)` |
| `client.workflows.processes()` / `.describe(p)` | `.processes()` / `.describe(process)` |
| `.start(p, key, payload)` | `.start(process, business_key, payload=None)` |
| `.status(p, key)` | `.status(process, key)` |
| `.decide(p, key, step, approved, reason?)` | `.decide(process, key, step, approved, reason=None)` |
| `.cancel(p, key)` | `.cancel(process, key)` |
| `.waitForCompletion(p, key, { intervalMs, timeoutMs })` | `.wait_for_completion(process, key, *, interval=..., timeout=...)` returns the last status on timeout |
| `.watch(p, key, opts)` -> `{ done, isFinished, latest, stop(), updates() }` | `.watch(process, key, ...)` -> `Watch` with `.done`, `.is_finished`, `.latest`, `.stop()`, `.updates()` |
| `.startAndWatch(p, key, payload, opts)` -> `{ started, watch }` | `.start_and_watch(...)` -> `(started, watch)` named tuple |
| `SseParser.push(chunk)` | `SseParser.push(chunk)` (exported; used by conformance tests) |

### Requirements

- **PY-API-1 (M)** `FoundgineClient` takes `base_url` and an optional `token`: a string, a sync callable, or an async callable
  returning a string. The callable is invoked per request, so rotating tokens work (TS: `token: async () => "tok"`).
- **PY-API-2 (M)** Requests send `Authorization: Bearer <token>` when a token exists. With no token the server answers 401 and
  the SDK raises `FoundgineHttpError(status=401)`; it does not pre-empt it.
- **PY-API-3 (M)** Session handling matches TS: initialize lazily and once; send `Mcp-Session-Id` on every later call;
  `tools/list` follows `nextCursor` until exhausted; concurrent first calls produce exactly one `initialize` (an
  `asyncio.Lock`; TS gets this for free from its event loop, Python must say it).
- **PY-API-4 (M)** `execute` returns a result object (`content`, `structured_content`, `is_error`, plus the raw dict). A tool
  that fails returns `is_error=True` as data; a protocol failure raises `FoundgineRpcError` (JSON-RPC `error`, e.g. -32601).
- **PY-API-5 (M)** `client.mcp` stays public (TS tests call `client.mcp.request`, `.session`, `.notifications`, `.initialize`).
- **PY-API-6 (M)** Path segments (`process`, `key`) are percent-encoded with nothing left unescaped (`quote(s, safe="")`). The TS
  tests start a workflow with the key `"k 1"`. Slashes, `?`, `#` and non-ASCII in a key must not change the route.
- **PY-API-7 (M)** `decide` posts exactly `{"step", "approved", "reason"}` and never an identity field. The server stamps
  `DecidedBy` from its trusted context (the shared route scenarios test `mallory` -> `alice`); the SDK must not offer a way to
  send one. When `reason` is omitted, [confirm in TS source] whether it is left out or sent as `null`, and do the same.
- **PY-API-8 (M)** Cancellation: cancelling the awaiting task during a call raises `asyncio.CancelledError` (never swallowed) and
  makes a best-effort, shielded `notifications/cancelled`. An optional `cancel: asyncio.Event` argument mirrors the TS
  `AbortSignal`: when set, the call raises `FoundgineAbortError` and sends the same notification. A cancel notification that
  itself fails must not mask the original outcome.
- **PY-API-9 (S)** A synchronous facade (`FoundgineSyncClient`) with the same method names, for scripts and notebooks. It runs
  the async core on a private event loop thread, is usable as a context manager, and raises the same exceptions. If cut from
  0.1.0, the core must not make it harder (no module-level event loop state).
- **PY-API-10 (M)** `watch` returns immediately (no I/O before the handle exists; TS asserts under 50 ms) and must be called
  inside a running loop (it creates a task). Handle: `done` (awaitable of the final status), `is_finished`, `latest`, `stop()`,
  `updates()` (async iterator replaying every distinct state from the start, even for a late consumer), and async context
  manager support that stops it on exit.
- **PY-API-11 (M)** `watch` options: `interval`, `timeout`, `on_update`, `on_error(error, consecutive_count)`,
  `not_found_grace`, `max_consecutive_errors`, `cancel`. Callbacks may be plain functions or coroutine functions; an exception
  in a callback must not kill the poll loop (log and continue) [confirm in TS source].
- **PY-API-12 (M)** Python-specific: if nobody awaits `done`, the SDK must not emit "Task exception was never retrieved" for
  an intentional `stop()`. `stop()` then `await done` raises `FoundgineAbortError`; `updates()` after stop raises it too.

## 4. Behaviour that must match TS exactly

These are the observable behaviours the TS tests pin. Each becomes a Python test (section 9).

### 4.1 Transport and errors
- **PY-ERR-1 (M)** Error classes and their relationships:
  `FoundgineError` (base) -> `FoundgineHttpError(status, message)` -> `FoundgineSessionExpiredError` (HTTP 404 on an MCP
  session call); `FoundgineResyncRequiredError` (stream GET 410); `FoundgineRpcError(code, message)`;
  `FoundgineAbortError`; `FoundgineWatchTimeoutError(last_status)`. TS tests use `instanceof` and `e.status`; Python tests use
  `isinstance` and `.status`. Whether `FoundgineAbortError`/`FoundgineWatchTimeoutError` extend `FoundgineError` [confirm in TS source].
- **PY-ERR-2 (M)** 401 -> `FoundgineHttpError` with `status == 401`; 404 on `/foundgine/...` -> `FoundgineHttpError(404)` (not
  `SessionExpired`; that class is only for MCP session calls and the stream GET).
- **PY-ERR-3 (M)** Expired session: the call raises `FoundgineSessionExpiredError`, the session is cleared, and the *next* call
  re-initializes automatically (TS test expects exactly two `initialize` requests).
- **PY-ERR-4 (M)** Stream GET statuses (`mcp-sse-transport-scenarios.json`): 400, 403, 409 -> `FoundgineHttpError` with that
  status and the session kept; 404 -> `FoundgineSessionExpiredError` and a fresh session on next use; 410 ->
  `FoundgineResyncRequiredError`.
- **PY-ERR-5 (C)** The TS 400-without-session-header message about `Access-Control-Expose-Headers` is a browser-CORS hint. It does
  not apply to Python; keep the status and a plain message. This is a deliberate divergence, to be listed in the README.
- **PY-ERR-6 (M)** Error `str()`/`repr()` never contain the token or the `Authorization` header.

### 4.2 SSE notifications
- **PY-SSE-1 (M)** `SseParser.push(chunk)` returns a list of `{id, event, data}`; handles chunk splits at any byte, CRLF/CR/LF,
  comment lines (`: hb`), multi-line `data:` joined with `\n`, and a single optional space after the colon.
- **PY-SSE-2 (M)** Pitfall specific to Python: do **not** use `str.splitlines()` or bare `str.strip()`. They split or strip on
  U+000B, U+000C, U+001C-U+001E, U+0085, U+2028 and U+2029, which would corrupt payloads the server sends raw. Split only on
  `\r\n`, `\r`, `\n`; remove only the one leading space after the colon. A lone `\r` at the end of a chunk followed by `\n` in
  the next must count as one line break. Test with 1-character chunks.
- **PY-SSE-3 (M)** Decode the byte stream with an incremental UTF-8 decoder, so multi-byte characters split across network
  chunks survive (TS uses a streaming `TextDecoder`). A BOM at stream start is ignored.
- **PY-SSE-4 (M)** An `event:` or `id:` text inside a data payload stays payload (the server escapes by line); only real field
  lines set fields. All conformance `format` vectors in `mcp-sse-scenarios.json` must pass whole, and in 1-char chunks.
- **PY-SSE-5 (M)** `notifications()` replays buffered events, remembers the last event id, and after the server ends the
  stream reconnects after `reconnect_delay` with `Last-Event-ID: <last id>`. The first GET carries no `Last-Event-ID` unless
  the caller passes `last_event_id`. Aborting ends the iteration cleanly (no exception), per the TS test.
- **PY-SSE-6 (M)** Resource safety: leaving the `async for` early (break, exception, cancel) closes the HTTP stream and frees
  the server's single stream slot (the server answers 409 if a second stream opens). Implement with `try/finally` and document
  `contextlib.aclosing` for callers.
- **PY-SSE-7 (M)** The stream request has no read timeout (heartbeat comments keep it alive); ordinary requests do (see PY-NFR-3).

### 4.3 Workflows
- **PY-WF-1 (M)** Routes (prefix `/foundgine/workflows`, as the TS test asserts):
  `GET /` processes, `GET /{process}` describe, `POST /{process}` start with body `{"businessKey", "payload"}`,
  `GET /{process}/{key}` status, `POST /{process}/{key}/decision`, `POST /{process}/{key}/cancel`.
- **PY-WF-2 (M)** Models (frozen dataclasses, `from_wire` parsers, tolerant of unknown fields, `None` for absent or null):
  - `StartResult(workflow_id, status, already_existed)`; `status` is `"Started"` or `"Existing"` (HTTP 202 / 200).
  - `ProcessDescription(name, steps)`, `StepDescription(name, requires_approval, has_compensation, approval_timeout_seconds | None)`.
    There are no operation names; the SDK must not invent them.
  - `WorkflowStatus(workflow_id, temporal_status, process | None, result | None)`.
  - `ProcessStatus(process, state, current_step, completed_steps, compensated_steps, pending_approval_step, failure)`.
  - `WorkflowResult(outcome, failure, outputs | None)`; `outputs` is absent unless the server enables `ExposeResultOutputs`.
- **PY-WF-3 (M)** `temporal_status` and `outcome` are strings with exported constants, not closed enums. Known outcomes:
  `Completed`, `Rejected`, `Failed`, `Cancelled`, `Terminated`, `TimedOut`. Known Temporal statuses include `Running`,
  `Completed`, `Failed`, `Canceled`, `Terminated`, `TimedOut`. An unknown value must pass through, never raise.
- **PY-WF-4 (M)** `wait_for_completion` polls until finished and returns the final status; on timeout it **returns the last status**
  rather than raising (TS asserts `Running`). `watch(...).done` on timeout **raises** `FoundgineWatchTimeoutError` carrying the last status.
- **PY-WF-5 (M)** `watch` semantics from the TS tests:
  - `on_update` fires on each *distinct* state, so identical consecutive `Running` polls collapse (`["Running", "Completed"]`).
  - Transient 5xx and an early 404 inside `not_found_grace` (default >0) are retried; `on_error` receives `1, 2, ...`.
  - More than `max_consecutive_errors` failures -> raise the last error. A 404 after the grace period is fatal. A **401 is fatal at once**, with no retry.
  - Polling stops immediately on `stop()` or `cancel`; no request is sent afterwards.
  - Finished means: [confirm in TS source] (candidate: a non-`Running` status or a non-null result). Check the `ContinuedAsNew` case: the C# route returns no `result` for any non-terminal-mapped status, so a rule based on `result` alone could loop forever.
- **PY-WF-6 (M)** `start_and_watch` starts, then watches in the background; `started.status == "Started"`; the watch result equals what a separate `watch` would give.
- **PY-WF-7 (S)** Poll interval uses the monotonic clock, not wall time, so clock changes cannot shorten or stretch it. Add small jitter only if the TS SDK does [confirm in TS source].

## 5. Python-specific requirements

- **PY-PKG-1 (M)** Python >= 3.11. (3.10 reaches end of life in October 2026, so it is already out of support. 3.11 gives
  `asyncio.timeout`, `TaskGroup`, `ExceptionGroup`.) CI matrix: 3.11, 3.12, 3.13, 3.14.
- **PY-PKG-2 (M)** One runtime dependency: `httpx` (async streaming, `MockTransport` for tests, connection pooling). No pydantic.
  Dev: `pytest`, `pytest-asyncio`, `pytest-cov`, `ruff`, `pyright` (strict) or `mypy --strict`.
- **PY-PKG-3 (M)** Layout mirrors TS: `src/python/foundgine-sdk/` with `pyproject.toml` (hatchling), `src/foundgine/` (src layout),
  `tests/`, `py.typed`. Distribution name `foundgine-sdk`, import name `foundgine`. **Open question:** confirm both names are
  free on PyPI (not checkable from here).
- **PY-PKG-4 (M)** Fully typed public API (`py.typed`), docstrings on every public symbol, `__all__` listing the public surface,
  no wildcard imports. Public classes are importable from `foundgine` directly.
- **PY-PKG-5 (M)** Resource ownership: if the caller passes `http_client`, the SDK never closes it; if it creates its own, `close()`
  does. No work at import time (no sockets, no threads, no event loop).
- **PY-PKG-6 (S)** `logging.getLogger("foundgine")`; no handlers attached; no secrets or request bodies at INFO or above.
- **PY-PKG-7 (S)** Optional `User-Agent: foundgine-python/<version>`; `__version__` exported and equal to the package metadata.

## 6. Non-functional requirements

- **PY-NFR-1 (M) Security.** Token only in the `Authorization` header; never in URLs, logs, `repr`, exceptions, or warnings.
  Redirects are not followed by default (`follow_redirects=False`), so a token cannot leak to another host. TLS verification
  stays on; no `verify=False` convenience flag.
- **PY-NFR-2 (S) Transport safety.** Warn once when a token is configured for a plain `http://` URL whose host is not loopback.
  Python-specific hardening, not in TS.
- **PY-NFR-3 (M) Timeouts.** httpx defaults to 5 s per operation, which would break slow tool calls. Set and document explicit
  defaults: connect 10 s, request/read 60 s, stream read unlimited; all overridable (`timeout=`). [confirm TS behaviour: `fetch` has none.]
- **PY-NFR-4 (M) Concurrency.** One client is safe to share across tasks. The session initialisation, session reset and event-id
  bookkeeping are protected against interleaving. At most one notification stream per session is a *server* rule; the SDK
  surfaces the 409 rather than hiding it.
- **PY-NFR-5 (M) Cancellation hygiene.** `CancelledError` always propagates; no `except Exception` that eats it; cleanup in
  `finally` is shielded where it must complete.
- **PY-NFR-6 (S) Performance.** No per-event overhead beyond parsing; stream parsing is O(n) with no repeated string concatenation
  of the whole buffer.
- **PY-NFR-7 (M) Compatibility.** Any server that passes the repo's conformance vectors works. The SDK sends `Accept` and
  `MCP-Protocol-Version` exactly as TS does [confirm in TS source; the TS mock's CORS list allows `mcp-protocol-version`].
- **PY-NFR-8 (S) Docs.** README with install, async and sync quick starts, workflow watch example, error table, and the list of
  deliberate divergences from the TS SDK (PY-ERR-5, `cancel` event instead of `AbortSignal`, task-based `watch`).

## 7. Testing requirements

- **PY-TEST-1 (M)** Port every TS test (parity matrix below). Behaviour assertions are the same; mechanics are Python's.
- **PY-TEST-2 (M)** Mock server: port `mock-server.ts` behaviour exactly (same routes, same sentinel keys `slow`, `done`, `flaky`,
  `late`, `hot`, `missing`, same request log). Two layers:
  1. `httpx.MockTransport` for fast request/response tests;
  2. a real loopback HTTP server (stdlib `asyncio` or `aiohttp` as a dev dependency) for SSE streaming, connection close,
     abort and the "no polling after stop" checks, because `MockTransport` cannot model a half-open stream faithfully.
- **PY-TEST-3 (M)** Conformance: load `src/conformance/mcp-sse-scenarios.json` (`format` steps) and
  `mcp-sse-transport-scenarios.json` (every status in a `get` step). Find the directory as TS does (relative to the repo, override
  with `FOUNDGINE_CONFORMANCE_DIR`). **Fail, do not skip**, when the vectors are missing in CI (TS skips silently; CI guards it).
- **PY-TEST-4 (M)** Python-only tests: the `splitlines` pitfall (U+2028, U+0085, U+000B in a payload), a multi-byte character split
  across chunks, a lone `\r` split from its `\n`, concurrent first calls produce one `initialize`, `CancelledError` propagates and
  closes the stream, an unawaited stopped `watch` emits no warning, the token never appears in any exception text.
- **PY-TEST-5 (S)** Cover what TS leaves uncovered (its coverage notes list `close()` failure and 404 handling branches in the
  session and stream code). Target >= 95% line coverage on the transport and workflow modules.
- **PY-TEST-6 (S)** Integration test against a real Foundgine server (C# or Java) started in CI, covering one list, start,
  status, decide, cancel round trip. Until a runnable server container exists this is a documented manual step.
- **PY-TEST-7 (S)** Shared client scenarios: move the request/response expectations (route table, bodies, header rules) into a
  JSON file in `src/conformance` read by both the TS and Python tests, so the two clients cannot drift. This needs a small TS
  test refactor, so it is a follow-up, not a blocker.

### Parity matrix (TS test -> Python test)

| TS test (`sdk.test.ts` unless noted) | Python test | Req |
|---|---|---|
| initializes once, bearer + session id, pages tools/list | `test_initializes_once_and_pages_tools` | PY-API-2/3 |
| execute passes args; tool failure is data; protocol failure throws | `test_execute_data_vs_rpc_error` | PY-API-4 |
| abort rejects and sends `notifications/cancelled` | `test_cancel_event_and_task_cancel_send_notification` | PY-API-8 |
| expired session typed error, next call re-initializes | `test_expired_session_reinitializes` | PY-ERR-3 |
| 401 is an HTTP error with status | `test_401_is_http_error` | PY-ERR-2 |
| SSE replays, resumes with `Last-Event-ID` | `test_sse_resume_with_last_event_id` | PY-SSE-5 |
| SSE 410 requires resync | `test_sse_410_requires_resync` | PY-ERR-4 |
| workflows: start/status/decide/cancel routes, 404 typed | `test_workflow_routes_and_bodies` | PY-WF-1, PY-API-6/7 |
| `SseParser` split chunks, CRLF, comments, multi-line | `test_sse_parser_basics` | PY-SSE-1 |
| 400 without session id (CORS hint) | not ported (PY-ERR-5); replaced by `test_400_without_session_is_http_error` | PY-ERR-5 |
| process listing and describe, 404 typed | `test_processes_and_describe` | PY-WF-2 |
| finished status carries result; `waitForCompletion` | `test_finished_status_and_wait_for_completion` | PY-WF-4 |
| watch: immediate, distinct updates, final result | `test_watch_basic` | PY-API-10, PY-WF-5 |
| watch: `updates()` for a late consumer | `test_watch_updates_late_consumer` | PY-API-10 |
| watch: `stop()` ends polling, `done` raises abort | `test_watch_stop` | PY-API-12 |
| watch: external signal stops it | `test_watch_cancel_event` | PY-API-8 |
| watch: tolerates 5xx and early 404 | `test_watch_tolerates_transient_errors` | PY-WF-5 |
| watch: gives up; late 404 fatal; 401 fatal, no retry | `test_watch_fatal_errors` | PY-WF-5 |
| watch: timeout rejects with last status; wait returns it | `test_watch_timeout_vs_wait` | PY-WF-4 |
| `startAndWatch` | `test_start_and_watch` | PY-WF-6 |
| `conformance.test.ts` SSE frame vectors (whole, 1-char, one stream) | `test_sse_format_vectors*` | PY-SSE-4 |
| `conformance.test.ts` gate statuses 400/403/409/404 | `test_stream_gate_statuses*` | PY-ERR-4 |
| `browser-check.mjs` (page, worker, service worker) | not applicable; replaced by loopback integration (PY-TEST-2) | n/a |

## 8. Build, CI and release

- **PY-CI-1 (M)** Add a `python` job to `.github/workflows/ci.yml` (matrix 3.11-3.14): `ruff check`, `ruff format --check`,
  type check, `pytest --cov`, then `python -m build` and `twine check` on 3.13. Fail if the conformance vectors are missing.
- **PY-CI-2 (M)** Publish on a tag `py-vX.Y.Z` after `python`, `sdk` (TS), `dotnet` and `java` all pass, because a Python release
  must not ship against a wire contract the servers fail. Same guards as the npm job: commit is on `main`, tag equals the package
  version, version not already on PyPI.
- **PY-CI-3 (M)** Publish the exact sdist and wheel the tests ran against (upload as an artifact, publish from it). Use PyPI
  trusted publishing (OIDC, `pypa/gh-action-pypi-publish`) with an `pypi` environment that can require approval; no long-lived token.
- **PY-CI-4 (S)** Pre-release versions (`0.1.0rc1`) go to PyPI as pre-releases, never as the default install.
- **PY-CI-5 (S)** Dependabot or equivalent for `httpx` and the GitHub Actions versions.

## 9. Plan

| Step | Output | Exit criterion |
|---|---|---|
| 0. Read TS `src/` | every **[confirm in TS source]** resolved; this file updated | open points closed |
| 1. Skeleton | `pyproject.toml`, package, errors, `SseParser`, lint/type CI | `SseParser` passes the conformance vectors |
| 2. MCP core | session, `list_capabilities`, `execute`, cancel, `notifications` | tests 1-7 of the matrix green |
| 3. Workflows | routes, models, `wait_for_completion`, `watch`, `start_and_watch` | whole matrix green |
| 4. Hardening | Python-only tests, coverage, security checks, docs | PY-TEST-4/5, PY-NFR-1..5 done |
| 5. Release pipeline | CI job, tag flow, first `0.1.0rc1` | rc installs from PyPI in a clean venv |
| 6. (S) Sync facade, shared client vectors, integration test | follow-up | |

Estimate: steps 1-4 are about the size of the TS SDK's test suite (roughly 240 lines of tests plus mock), so mostly mechanical once
step 0 is done; the real risk is `watch` and SSE cancellation semantics, not volume.

## 10. Open questions (recommendation first)

1. **Names.** Recommend dist `foundgine-sdk`, import `foundgine`. Need a PyPI availability check and your sign-off.
2. **Versioning.** Recommend the Python and TS SDKs share major.minor (wire contract) and patch independently.
3. **Sync API in 0.1.0?** Recommend yes if step 3 finishes early, otherwise 0.2 (PY-API-9).
4. **Cancellation parameter.** Recommend an optional `asyncio.Event` named `cancel` plus normal task cancellation, over inventing an
   `AbortSignal` clone.
5. **Dependency on `httpx` only?** Recommend yes. `aiohttp` would be the only alternative worth considering; httpx wins on
   `MockTransport` and HTTP/2 later.
6. **Public vs private package.** The TS package is `"license": "UNLICENSED"`. Decide the Python license and publication
   visibility before step 5; PyPI uploads are effectively permanent.
7. **Definition of "finished" for `watch`** and the `ContinuedAsNew` edge case (PY-WF-5): needs the TS source and a server-side
   decision, since the C# and Java status routes return no `result` for statuses they do not map.
8. **Where the integration server comes from** (PY-TEST-6): is there a runnable server image, or should CI build the C# host?
