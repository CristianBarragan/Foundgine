# Client SDKs: TypeScript, Python and WebAssembly

Foundgine ships three **thin transport clients** for talking to a running Foundgine server from outside .NET or Java. They are in the repository today under `src/typescript/`, `src/python/` and `src/wasm/`.

> **In plain words.** The .NET and Java implementations *are* Foundgine: they hold the semantic model, authorization, planning and providers. The TypeScript, Python and WebAssembly SDKs are *remote controls*. They speak MCP and the Temporal workflow HTTP routes to a Foundgine host that you run. They never resolve intent, authorize anything or touch a database.

## At a glance

| | TypeScript | Python | Rust / WebAssembly |
|---|---|---|---|
| Distribution name | `@foundgine/typescript` (npm) | `foundgine-sdk` (PyPI name in metadata) | `@foundgine/wasm` (npm); Rust crate `foundgine-sdk-wasm` (`publish = false`) |
| Import | `import { FoundgineClient } from "@foundgine/typescript"` | `from foundgine import FoundgineClient` | `import { FoundgineClient, init } from "@foundgine/wasm"` |
| Source | [`src/typescript/`](../../src/typescript/README.md) | [`src/python/`](../../src/python/README.md) | [`src/wasm/`](../../src/wasm/README.md) |
| Runtime | Node.js 20+ or any runtime with `fetch` | Python 3.11+, `httpx` | Node.js 20+, browsers, Workers, Deno (host supplies `fetch`) |
| Style | async, `camelCase`, `AbortSignal` | async **and** sync clients, `snake_case`, `asyncio.Event` cancel | async, `camelCase`, `AbortSignal`; pure-Rust sans-I/O core |
| Version | `3.3.5` | `3.3.5` | `3.3.5` |
| CI workflow | `typescript-build.yml` (Node 20, 22) | `python-build.yml` (3.11–3.14, ruff, mypy) | `wasm-build.yml` (Rust 1.75, wasm-bindgen, Node 20) |

All three are marked in their own READMEs as **development packages, not release-ready SDKs**. The repository does not claim that a registry release is available; check npm or PyPI before pinning a version.

## What every SDK does

- **MCP over Streamable HTTP**: session initialization (`initialize` then `notifications/initialized`, once even under concurrency), JSON-RPC requests, paged capability listing and tool execution. Default endpoint path `/mcp`, default protocol version `2025-06-18` (both overridable).
- **Notification stream**: SSE with `Last-Event-ID` resume and cooperative cancellation.
- **Temporal workflow routes** under `/foundgine/workflows`: catalog (`processes`, `describe`), `start`, `status`, `decide`, `cancel`, plus the helpers `waitForCompletion` / `wait_for_completion`, `watch` and `startAndWatch` / `start_and_watch`.
- **Typed errors**: `FoundgineError`, `FoundgineHttpError`, `FoundgineRpcError`, `FoundgineSessionExpiredError`, `FoundgineResyncRequiredError`, `FoundgineAbortError`, `FoundgineWatchTimeoutError`.
- **Per-request tokens**: `token` is a string or a sync/async provider, resolved on every request. Redirects are not followed.

## What no SDK does

- It does not embed the semantic runtime, model, planner, providers or ExecutionIR.
- It does not decide identity, tenant or authorization. The server stamps those from its trusted security context. `decide` has no caller-identity field, and clients must not send tenant or authorization predicates.
- It does not make a tool call safe. Discovery through `listCapabilities` is advisory; every call is resolved and authorized again on the server.

## Quick start

The server exposes these MCP tools (see `Foundgine.Providers/Tools/MCP`): `foundgine_capabilities`, `foundgine_query` (open intent, natural language or JSON), `foundgine_query_semantic` (already-structured JSON read intent, no lexical grounding), `foundgine_mutation_dry_run`, `foundgine_mutation_approve` and `foundgine_mutation_execute_approved`. Query tools take a single `intentJson` string argument whose JSON holds `rootEntity`, `selections` (objects such as `{ "field": "Name" }`) and optional `filter`/`order`/`limit`/`offset`/`after`. Always read `foundgine_capabilities` first to learn the entities and fields available to the caller.

**TypeScript**

```ts
import { FoundgineClient } from "@foundgine/typescript";

const client = new FoundgineClient({
  baseUrl: "https://your-host.example",
  token: async () => process.env.FOUNDGINE_TOKEN,
});
try {
  const contract = await client.execute("foundgine_capabilities", {});
  const rows = await client.execute("foundgine_query_semantic", {
    intentJson: JSON.stringify({ rootEntity: "Customer", selections: [{ field: "Name" }], limit: 20 }),
  });
  console.log(contract.content, rows.content, rows.isError);
} finally {
  await client.close();
}
```

**Python**

```python
import asyncio, json
from foundgine import FoundgineClient

async def main() -> None:
    async with FoundgineClient("https://your-host.example", token=lambda: "tok") as client:
        rows = await client.execute(
            "foundgine_query_semantic",
            {"intentJson": json.dumps({"rootEntity": "Customer", "selections": [{"field": "Name"}], "limit": 20})},
        )
        print(rows.content, rows.is_error)

asyncio.run(main())
```

**WebAssembly (JavaScript host)**

```js
import { FoundgineClient, init } from "@foundgine/wasm";

await init(); // load the .wasm once
const client = new FoundgineClient({ baseUrl: "https://your-host.example", token: async () => "tok", fetch });
const rows = await client.execute("foundgine_query_semantic", {
  intentJson: JSON.stringify({ rootEntity: "Customer", selections: [{ field: "Name" }], limit: 20 }),
});
```

The `rootEntity` and `selections` values above are placeholders; use the names returned by `foundgine_capabilities` for your model. `result.isError` / `result.is_error` is data, not an exception: a tool-level failure comes back in the result.

## Choosing an SDK

| If you are... | Use |
|---|---|
| Building a Node, Bun or browser app, or an agent in TypeScript | **TypeScript** |
| Scripting, notebooks, data or ML pipelines, Python agents | **Python** (`FoundgineSyncClient` for scripts and Jupyter) |
| Targeting browsers, Workers or Deno and want one audited core shared with native Rust | **WebAssembly** |
| Writing server-side .NET or Java that hosts Foundgine | None of these. Use the .NET or Java packages ([package guide](../../docs-site/packages/index.md)) |

## Deliberate differences between the SDKs

- **Naming and units.** TypeScript and WebAssembly use `camelCase` and milliseconds (`intervalMs`, `timeoutMs`). Python uses `snake_case` and seconds (`interval`, `timeout`).
- **Cancellation.** TypeScript and WebAssembly take an `AbortSignal`; Python takes an optional `asyncio.Event` named `cancel` (task cancellation also works and sends `notifications/cancelled`).
- **WebAssembly specifics.** Call `init()` first; `decide` takes positional arguments; `notifications()` lives on `client.mcp`; `listTools()` and `callTool()` remain as aliases of `listCapabilities()` and `execute()`.
- **Full tables.** The [Python README](../../src/python/README.md) lists every Python/TypeScript difference and the [WASM README](../../src/wasm/README.md) lists the layered architecture and parity gaps.

## Verification status

| Evidence | Where |
|---|---|
| Unit tests per SDK | `src/typescript/test/`, `src/python/tests/`, `src/wasm/tests/` and `src/wasm/test/` |
| Shared SSE vectors (`mcp-sse-scenarios.json`, `mcp-sse-transport-scenarios.json`) | [`src/conformance/`](../../src/conformance/README.md) |
| CI definitions | `.github/workflows/typescript-build.yml`, `python-build.yml`, `wasm-build.yml` |
| Not yet verified | Integration against a real Foundgine server (Python and WASM READMEs); browser, Worker and Service Worker checks (TypeScript); registry installs |

A workflow definition is not proof that a given run passed. The SDK READMEs and source are authoritative.

## See also

- [AI agents and MCP](agents-mcp.md) and [MCP protocol versions, sessions and approval tokens](mcp-protocol-and-tokens.md): what the server side does with these calls.
- [Extensions](extensions.md): the Temporal extension that serves the workflow routes the SDKs call.
- [Cross-language implementation map](implementation-map.md) and [Source coverage](../SOURCE-COVERAGE.md).
