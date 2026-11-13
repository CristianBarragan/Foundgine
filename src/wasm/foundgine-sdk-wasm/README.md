# foundgine-sdk-wasm (Rust, WebAssembly)

Thin WebAssembly client core for Foundgine MCP and Temporal workflow HTTP transports. It mirrors
[`@foundgine/sdk`](../../typescript/foundgine-sdk) and [`foundgine-sdk`](../../python/foundgine-sdk) module for
module. It hosts no MCP server and has no business logic.

**Status: development, checkpoint 4.** The Rust core, the wasm-bindgen layer and the ES-module wrapper are built and
tested together: 53 Rust unit tests, and a Node harness (29 tests) that drives the compiled `.wasm` against a real
in-process HTTP server. Not yet run in CI or against a real Foundgine server.

```sh
cd src/wasm/foundgine-sdk-wasm
cargo test                                   # native; also runs the shared SSE vectors
rustup target add wasm32-unknown-unknown
cargo install wasm-bindgen-cli --version 0.2.92 --locked   # must equal the wasm-bindgen crate version (=0.2.92)
npm run build                                # cargo build --release --target wasm32 + wasm-bindgen -> pkg/
npm test                                     # Node >= 20: the harness in test/
```

## Parity map

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

## Using it from JavaScript

```js
import { FoundgineClient, init } from "@foundgine/sdk-wasm";

await init();                                   // loads the .wasm once (Node reads it from disk, browsers fetch it)
const client = new FoundgineClient({
  baseUrl: "https://foundgine.example.com",
  token: async () => getToken(),                // a string, or a function called once per HTTP request
  fetch,                                        // optional; defaults to globalThis.fetch
});

const tools = await client.listCapabilities();  // canonical cross-SDK name; follows nextCursor
const result = await client.execute("echo", { text: "hi" });   // result.isError is data, not a throw
// Backwards-compatible aliases: client.listTools() and client.callTool(...)

for await (const message of client.mcp.notifications({ signal })) { /* server-to-client messages */ }

const { watch } = await client.workflows.startAndWatch("order", "order-17", { sku: 1 }, { intervalMs: 1000 });
for await (const status of watch.updates()) console.log(status.temporalStatus);
await client.close();
```

The layers, from the bottom: pure Rust modules (no I/O) -> `src/bindings.rs` (wasm-bindgen; JSON strings in and out,
errors thrown as JSON `{name, message, status?, ...}`) -> `js/` (everything that needs `fetch`, a clock or an
`AbortSignal`). `pkg/` is build output and is not committed.

## Remaining choices and parity gaps

The TypeScript and Python sources are now available in the same repository. The following details remain intentionally
different or need explicit parity work:

- `workflows.decide(process, key, step, approved, { reason })` takes positional arguments (as `decision_request` does).
- `watch.stop()` rejects `done` with `FoundgineAbortError`; `watch.updates()` throws the failure (including an abort) once the history is replayed.
- `mcp.notifications()` throws `FoundgineAbortError` when its signal aborts, and reconnects only after a clean end of stream (not after errors).
- `mcp.request()` on abort sends a best-effort `notifications/cancelled` and throws `FoundgineAbortError`.
- The handshake resolves the token separately for `initialize` and `notifications/initialized` (per HTTP request).
- Status objects gain an `isFinished` field; `describe()` steps use `null` (not `undefined`) for a missing `approvalTimeoutSeconds`.

## Design

- **Sans-IO core.** Request building, response interpretation, SSE parsing, error mapping and the workflow
  state machine are pure Rust. The same code runs natively (tests) and in `wasm32`.
- **Host-injected I/O.** Like the TS `fetch` option, the JS host passes its own `fetch` (browser, Worker, Deno,
  Node 20+; tested on Node 22 here, CI uses 20). The crate never opens sockets, so there is no WASI networking dependency.
- **Wire JSON stays camelCase**; the Rust API is snake_case, as in Python.

## Remaining deliberate differences and parity gaps

- Non-JSON error bodies are cut at 200 Unicode scalar values (TS: 200 UTF-16 code units).
- Watch de-duplication compares the raw status object ignoring key order (TS compares `JSON.stringify`, which is key-order sensitive).
- `retry:` values too large for `u64` are ignored (TS keeps a float).
