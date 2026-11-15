# @foundgine/wasm (Rust, WebAssembly)

A thin WebAssembly client core for Foundgine MCP and Temporal workflow HTTP transports.

> Part of the Foundgine repository. This client talks to a running Foundgine server (the .NET or Java implementation hosting the MCP and Temporal endpoints); it does not contain the semantic runtime. See the [client SDK overview](../../docs/wiki/client-sdks.md) and the [source layout](../README.md).

Requires Rust (`wasm32-unknown-unknown` target), `wasm-bindgen-cli` (`=0.2.92`), and Node.js 20+.

## Install

```sh
cd src/wasm/
cargo test                                   # native; also runs the shared SSE vectors
rustup target add wasm32-unknown-unknown
cargo install wasm-bindgen-cli --version 0.2.92 --locked   # must match the wasm-bindgen crate version (=0.2.92)
npm run build                                # cargo build --release --target wasm32 + wasm-bindgen -> pkg/
npm test                                     # Node >= 20: runs the harness in test/
```

## MCP client

```js
import { FoundgineClient, init } from "@foundgine/wasm";

await init(); // loads the .wasm once (Node reads it from disk, browsers fetch it)
const client = new FoundgineClient({
  baseUrl: "https://example.invalid",
  token: async () => process.env.FOUNDGINE_TOKEN,
  fetch, // optional; defaults to globalThis.fetch
});

try {
  const tools = await client.listCapabilities(); // canonical cross-SDK name; follows nextCursor
  // Query tools take one `intentJson` string; names come from foundgine_capabilities.
  const result = await client.execute("foundgine_query_semantic", {
    intentJson: JSON.stringify({ rootEntity: "Customer", selections: [{ field: "Name" }], limit: 20 }),
  }); // result.isError is data, not a throw
  console.log(tools, result.content, result.isError);
} finally {
  await client.close();
}
```

`token` may be a string or a sync/async provider; it is resolved for every HTTP request (separately for `initialize` and `notifications/initialized`). The SDK uses the supplied `fetch` implementation when provided, otherwise `globalThis.fetch`. Backwards-compatible aliases `client.listTools()` and `client.callTool(...)` are also provided.

## Notifications

```js
const controller = new AbortController();
for await (const message of client.mcp.notifications({ signal: controller.signal })) {
  console.log(message);
}
```

The notification stream resumes using `Last-Event-ID` after a clean end of stream (not after errors). Stop it with the supplied `AbortSignal`, which throws `FoundgineAbortError`.

## Temporal workflows

The workflow client follows the HTTP routes exposed by Foundgine Temporal:

```js
const { started, watch } = await client.workflows.startAndWatch(
  "order-approval",
  "order-1042",
  { orderId: "order-1042" },
  { intervalMs: 1_000, timeoutMs: 120_000 },
);
console.log(started.workflowId);
for await (const status of watch.updates()) {
  console.log(status.temporalStatus);
}
const finalStatus = await watch.done;
```

The catalog can also be inspected before starting work:

```js
const names = await client.workflows.processes();
const definition = await client.workflows.describe(names[0]);
console.log(definition.steps.map((step) => [step.name, step.requiresApproval]));
```

Also available: `processes`, `describe`, `start`, `status`, `decide`, `cancel`, `waitForCompletion`, and `watch`. Catalog descriptions expose safe step metadata only; internal operation identifiers are not returned by the server. `watch()` returns immediately; its `updates()` iterator replays distinct observed states to late consumers. `watch.stop()` rejects `done` with `FoundgineAbortError`, and `watch.updates()` throws the failure (including an abort) once the history is replayed. Process and business-key path segments are percent-encoded. `decide` has no caller-identity field: the server stamps the decision subject from its trusted security context.

## Architecture and parity map

- **Sans-IO core.** Request building, response interpretation, SSE parsing, error mapping, and the workflow state machine are pure Rust (`src/`). The same code runs natively in unit tests and compiles to `wasm32`.
- **Host-injected I/O.** Like `@foundgine/typescript`'s `fetch` option, the JS host passes its own `fetch` (browser, Worker, Deno, or Node 20+). The crate never opens sockets, so there is no WASI networking dependency.
- **Layered boundary.** Pure Rust modules (no I/O) $\rightarrow$ `src/bindings.rs` (`wasm-bindgen`; JSON strings in and out, errors thrown as JSON `{name, message, status?, ...}`) $\rightarrow$ `js/` wrapper (everything requiring `fetch`, a clock, or an `AbortSignal`). `pkg/` is build output and is not committed.
- **Wire JSON stays camelCase**; the Rust API is `snake_case`, as in Python.

| TypeScript | Python | Rust (this crate) | Checkpoint |
|---|---|---|---|
| `errors.ts` (class hierarchy) | `errors.py` | `errors.rs` (`FoundgineError` enum, `is_http_error()`) | 1 |
| `sse.ts` `SseParser` | `sse.py` | `sse.rs` `SseParser` (`push_bytes`, `push_str`) | 1 |
| `http.ts` | `http.py` | `http.rs` (`join_url`, `encode_path_segment`, `error_for`, token helpers) | 1 |
| `mcp.ts` `McpSession` | `mcp.py` | `mcp.rs` `McpProtocol` (request builders + response interpreters, no I/O) | 2 |
| `client.ts` `FoundgineClient` | `client.py` | `client.rs` (URL check, `tools/list` paging and `tools/call` shapes) | 2 |
| `workflows.ts` `WorkflowClient` | `workflows.py` | `workflows.rs` (`WorkflowRoutes`, parsers, `WatchPoller`/`WaitPoller` with an injected clock) | 3 |
| `fetch` option | `http_client=` | `bindings.rs` (JSON-string boundary) + `js/` wrapper: injected `fetch`, tokens, abort, sleeping, stream reading | 4 |
| `FoundgineClient`, `client.mcp`, `client.workflows`, async iterators | same shape | `js/client.js`, `js/mcp.js`, `js/workflows.js`, typed in `js/index.d.ts` | 4 |

## Deliberate differences and parity gaps

- `workflows.decide(process, key, step, approved, { reason })` takes positional arguments (matching `decision_request`).
- `watch.stop()` rejects `done` with `FoundgineAbortError`; `watch.updates()` throws the failure (including an abort) once the history is replayed.
- `mcp.notifications()` throws `FoundgineAbortError` when its signal aborts, and reconnects only after a clean end of stream (not after errors).
- `mcp.request()` on abort sends a best-effort `notifications/cancelled` and throws `FoundgineAbortError`.
- The handshake resolves the token separately for `initialize` and `notifications/initialized` (per HTTP request).
- Status objects gain an `isFinished` field; `describe()` steps use `null` (not `undefined`) for a missing `approvalTimeoutSeconds`.
- Non-JSON error bodies are truncated at 200 Unicode scalar values (`@foundgine/typescript` uses 200 UTF-16 code units).
- Watch de-duplication compares the raw status object ignoring key order (`@foundgine/typescript` compares `JSON.stringify`, which is key-order sensitive).
- `retry:` values too large for `u64` are ignored; `@foundgine/typescript` retains the value as a JavaScript number.

## Status and known limits

Implemented and tested (checkpoint 4): typed errors, SSE parser (including shared SSE vectors), MCP session initialization, JSON-RPC requests, paged capability listing, tool execution, cancellation, resumable notification streaming, Temporal start/status/decision/cancel routes, workflow polling/watch helpers, the `wasm-bindgen` layer, and the ES-module wrapper—backed by 53 Rust unit tests and a 29-test Node harness driving the compiled `.wasm` against a real in-process HTTP server.

Not yet done or verified: running in CI or against a real Foundgine server. Developed and tested on Node 22 (CI targets Node 20). This is a development package, not a release-ready SDK.