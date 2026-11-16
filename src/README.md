# Source layout

Foundgine keeps each language implementation under its own language-specific
source root:

```text
src/
├── csharp/                         # .NET 10 implementation
│   ├── Foundgine.Core/
│   ├── Foundgine.Runtime/
│   ├── Foundgine.Providers/             # includes Redis and Microsoft Agent Framework adapters
│   └── Foundgine.Extensions/
├── java/                           # Java 27 Maven reactor
│   ├── pom.xml
│   ├── foundgine-core/
│   ├── foundgine-runtime/
│   ├── foundgine-providers/             # includes Redis, Spring Boot and LangChain4j adapters
│   ├── foundgine-extensions/
│   ├── foundgine-migrations/
│   ├── foundgine-aot-generator/     # build-time module; not published separately
│   ├── security/foundgine-redteam/
│   └── samples/
├── conformance/                    # shared cross-language fixtures and generators
├── typescript/                     # @foundgine/typescript  — client SDK (npm)
├── python/                         # foundgine-sdk          — client SDK (PyPI name)
└── wasm/                           # @foundgine/wasm        — client SDK (Rust core + ES-module wrapper)
```

## Agent framework and Spring integrations

| Integration | Path | README | Status |
|---|---|---|---|
| Microsoft Agent Framework | `src/csharp/Foundgine.Providers/AgentFramework/` | [README](csharp/Foundgine.Providers/AgentFramework/README.md) | Included in `Foundgine.Providers` |
| LangChain4j | `src/java/foundgine-providers` | [README](java/foundgine-providers/LANGCHAIN4J.md) | Included in `foundgine-providers` |
| Spring Boot | `src/java/foundgine-providers` | [README](java/foundgine-providers/SPRING-BOOT.md) | Included in `foundgine-providers` |

They adapt Foundgine's governed tools and hosting to each framework; identity stays host-owned and writes stay gated. Overview: [`docs/wiki/agent-framework-integrations.md`](../docs/wiki/agent-framework-integrations.md).

## Client SDKs: TypeScript, Python and WebAssembly

The three SDK directories are **not** ports of the semantic runtime. They are thin transport clients for a running Foundgine server (the .NET or Java implementation): MCP over Streamable HTTP (default path `/mcp`, protocol version `2025-06-18`) and the Temporal workflow routes under `/foundgine/workflows`. Identity, tenant, authorization, planning and providers stay on the server.

| SDK | Directory | Package | Runtime | README |
|---|---|---|---|---|
| TypeScript | `src/typescript/` | `@foundgine/typescript` (npm) | Node.js 20+ or any runtime with `fetch` | [`typescript/README.md`](typescript/README.md) |
| Python | `src/python/` | `foundgine-sdk` (PyPI name) | Python 3.11+, `httpx`; async and sync clients | [`python/README.md`](python/README.md) |
| Rust / WebAssembly | `src/wasm/` | `@foundgine/wasm` (npm) | Node.js 20+, browsers, Workers, Deno; host supplies `fetch` | [`wasm/README.md`](wasm/README.md) |

Each SDK has the same shape: `FoundgineClient` (`listCapabilities` / `list_capabilities`, `execute`, `notifications`, `close`) plus a `workflows` client (`processes`, `describe`, `start`, `status`, `decide`, `cancel`, `waitForCompletion`, `watch`, `startAndWatch`) and typed errors. A minimal call looks like this (TypeScript shown; the Python and WASM READMEs have their own forms):

```ts
import { FoundgineClient } from "@foundgine/typescript";

const client = new FoundgineClient({ baseUrl: "https://your-host.example", token: async () => process.env.FOUNDGINE_TOKEN });
const contract = await client.execute("foundgine_capabilities", {});
await client.close();
```

How they relate to each other and to the servers:

- `src/typescript/` is the reference API. `src/python/` and `src/wasm/` follow it, with documented, deliberate differences (naming, units, cancellation).
- `src/wasm/` keeps a sans-I/O Rust core in `src/wasm/src/`, a `wasm-bindgen` layer in `bindings.rs` and a JavaScript wrapper in `src/wasm/js/`. `pkg/` is build output and is not committed.
- All three read shared SSE vectors from `src/conformance/` (`mcp-sse-scenarios.json`; the Python tests also use `mcp-sse-transport-scenarios.json`).
- Each has its own CI workflow under `.github/workflows/` (`typescript-build.yml`, `python-build.yml`, `wasm-build.yml`).

Build and test, from each SDK directory:

```bash
# TypeScript (Node 20+)
cd src/typescript && npm ci && npm test

# Python (3.11+)
cd src/python && pip install -e ".[dev]" && ruff check . && mypy && pytest

# Rust / WebAssembly (Rust 1.75, wasm32-unknown-unknown, wasm-bindgen-cli =0.2.92, Node 20+)
cd src/wasm && cargo test && npm run build && npm test
```

All three are development packages, not release-ready SDKs; see each README's "Status and known limits". For the combined overview, the "which one do I use" guide and the verification table, read [`docs/wiki/client-sdks.md`](../docs/wiki/client-sdks.md).

## Why this layout?

`src/` is the common source boundary for Foundgine. The language is explicit
at the first level, so the repository can grow a complete Java implementation
without mixing Java and C# projects in the same directory.

- `src/csharp/` contains the existing .NET solution projects.
- `src/java/` contains the Maven reactor, server modules, optional integrations, migration tooling, security module and samples.
- `src/conformance/` contains shared cross-language fixtures and their generators; see its [README](conformance/README.md).
- Redis and Microsoft Agent Framework adapters compile into the C# `Foundgine.Providers` package. Java Redis, Spring Boot and LangChain4j adapters compile into the Maven `foundgine-providers` artifact.
- C# tests remain under `src/csharp/tests/`.
- Java tests follow Maven convention inside each Java module under
  `src/test/java/`.

- `src/typescript/`, `src/python/` and `src/wasm/` contain the **client SDKs** described below.
- The optional framework integrations are `src/csharp/Foundgine.Providers/AgentFramework` (Microsoft Agent Framework), `src/java/foundgine-providers` (LangChain4j and Spring Boot); see below.

The two implementations can therefore evolve side-by-side while preserving
the same conceptual Foundgine module boundaries.
