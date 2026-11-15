# Foundgine 3.3.5 (.NET & Java) — Website full context

Foundgine is a **semantic execution layer for .NET 10 and Java 27**. It resolves structured caller intent into authorized, deterministic execution plans within an application-controlled boundary, using language-idiomatic mechanisms in each implementation. Thin TypeScript, Python and WebAssembly client SDKs for a running server are included under `src/typescript/`, `src/python/` and `src/wasm/` (see *Client SDKs* below). Optional Microsoft Agent Framework (.NET), LangChain4j and Spring Boot (Java) integrations are documented in *Agent framework and Spring integrations*.

## Canonical lifecycle

```text
Caller → Intent → Semantic Model → Semantic Operation Graph
       → Retrieval → Resolution → Authorization → Plan Binding
       → ExecutionIR → Provider → Execution → Evidence
```

**Retrieval is discovery, not authority.**

## Semantic execution lifecycle

1. **External actors and transports** — application code, MCP, GraphQL and JSON carry structured requests to the host-controlled boundary. Transport adapters do not own domain authority.
2. **Intent resolution** — the request is parsed and normalized into a typed intent and operation shape.
3. **Semantic model** — application metadata defines entities, fields, relationships, identities, aliases and exposed capabilities.
4. **Grounding and ambiguity resolution** — candidate discovery is evaluated against semantic evidence; unresolved ambiguity is rejected or returned for clarification rather than guessed.
5. **Authorization and capability boundaries** — trusted host context and policy determine which resolved operations the caller may exercise. Retrieval is never authorization.
6. **Planning and Execution IR** — the authorized logical operation becomes a deterministic provider-independent plan and a constrained execution representation.
7. **Runtime and provider execution** — runtime orchestration invokes a conforming provider; providers execute the authorized artifact rather than reinterpret caller authority.
8. **Results and observability** — results, execution evidence, receipts and diagnostics are returned without leaking secrets or elevating provider output into authority.

## Core vocabulary

- **Semantic model** — application-defined meaning: entities, fields, relationships, aliases and capabilities.
- **Intent** — what the caller asks for, independently of physical provider operations.
- **Retrieval** — candidate discovery plus evidence.
- **Resolution** — determines the intended semantic meaning.
- **Authorization** — determines what the trusted execution context may exercise.
- **Plan** — provider-independent logical execution.
- **ExecutionIR** — controlled intermediate representation at the provider boundary.
- **Provider** — physical execution implementation.
- **Evidence** — security/plan/execution context produced by the operation.

## AI and MCP

AI agents are untrusted callers. MCP and AI integrations expose capabilities but do not grant authority. Identity, tenant context, secrets and application policy remain host-owned. MCP adapters exist in both .NET and Java; GraphQL adapters exist in both implementations (Hot Chocolate for .NET and GraphQL Java for Java); Microsoft.Extensions.AI is .NET-specific. A Microsoft Agent Framework adapter exists in the post-3.3.5 working tree, but it has not yet been compiled or tested and is not a released capability.

## Language-specific implementation status

Both .NET and Java core implementations include semantic lexical grounding and clarification outcomes. The Java Advanced sample's direct PostgreSQL candidate-search MCP tool is separate from, and is not wired through, the Java core `SemanticLexicalResolver`. Java's optional `foundgine-spring` and `foundgine-langchain4j` modules have source and tests, but dependency pins and runtime verification remain pending. The Java `controlplane` package provides tool governance, approvals, audit, risk scoring and authorization-key rotation; it is not feature-equivalent to the wider .NET authority-recovery module. The `foundgine-migrations` module generates PostgreSQL/Flyway schema migrations from metadata; its Maven/runtime verification is pending. The latest reported Maven run failed in the Java Advanced sample test phase; fixes are present but have not yet been rerun. `foundgine-aot-generator` is build-time tooling, not a standalone runtime artifact.

## Agent framework and Spring integrations (MAF, LangChain4j, Spring Boot)

Optional adapters that put Foundgine behind agent frameworks or host it in Spring. The framework handles conversation or hosting; Foundgine still resolves, authorizes and executes, and the host supplies identity (`ISecurityExecutionContextProvider`), never the model.

| Integration | Language | Module / path | Provides |
|---|---|---|---|
| Microsoft Agent Framework (MAF) | .NET | `Foundgine.Providers.AgentFramework` (`src/csharp/Foundgine.Providers.AgentFramework/`) | `AddFoundgineAgentFramework()`, `FoundgineAgentTools` (Microsoft.Extensions.AI `AITool`s), `FoundgineAgentFactory`; writes off by default and human-approved via MAF |
| LangChain4j | Java | `foundgine-langchain4j` (`src/java/foundgine-langchain4j/`) | `FoundgineLangChain4jTools.toolMap(FoundgineMcpServer)` returning LangChain4j `ToolSpecification`/`ToolExecutor` pairs for `AiServices` |
| Spring Boot | Java | `foundgine-spring` (`src/java/foundgine-spring/`) | Auto-configuration for the MCP server and `POST /mcp` controller (sessions, SSE; property prefix `foundgine.mcp.*`, off by default) and for the JPA `FoundgineOrm` provider |

- **Status:** source and tests present for all three; **not yet build- or runtime-verified** in this snapshot. The MAF adapter is a separate packable project outside `Foundgine.sln` and is unreleased (post-3.3.5). `foundgine-langchain4j` and `foundgine-spring` are in the Maven reactor with dependency pins (LangChain4j 1.0.0, Spring Boot 4.0.0) pending verification. Do not describe them as released or verified.
- **Docs:** `docs/wiki/agent-framework-integrations.md`, plus each module's `README.md`.

## Client SDKs (TypeScript, Python, WebAssembly)

Three thin **transport clients** for a running Foundgine server live in this repository. They are not ports of the semantic runtime and they hold no authority: identity, tenant, authorization, planning and providers stay on the .NET or Java server.

| SDK | Directory | Package | Import | Runtime |
|---|---|---|---|---|
| TypeScript | `src/typescript/` | `@foundgine/typescript` (npm) | `import { FoundgineClient } from "@foundgine/typescript"` | Node.js 20+ or any runtime with `fetch` |
| Python | `src/python/` | `foundgine-sdk` (PyPI name) | `from foundgine import FoundgineClient` (also `FoundgineSyncClient`) | Python 3.11+, `httpx` |
| Rust / WebAssembly | `src/wasm/` | `@foundgine/wasm` (npm); crate `foundgine-sdk-wasm` is `publish = false` | `import { FoundgineClient, init } from "@foundgine/wasm"` | Node.js 20+, browsers, Workers, Deno; host supplies `fetch` |

- **Protocols:** MCP over Streamable HTTP (default path `/mcp`, default protocol version `2025-06-18`) and the Temporal workflow HTTP routes under `/foundgine/workflows` (`processes`, `describe`, `start`, `status`, `decide`, `cancel`, plus `waitForCompletion`, `watch`, `startAndWatch`).
- **Main calls:** `listCapabilities()` / `list_capabilities()`, `execute(toolName, args)`, `notifications()`, `close()`, and the `workflows` client.
- **Server MCP tools they call:** `foundgine_capabilities`, `foundgine_query` (open intent), `foundgine_query_semantic` (structured JSON intent), `foundgine_mutation_dry_run`, `foundgine_mutation_approve`, `foundgine_mutation_execute_approved`. Query tools take one `intentJson` string (`rootEntity`, `selections`, optional `filter`/`order`/`limit`/`offset`/`after`). Discovery via `foundgine_capabilities` is advisory and is not authorization.
- **Status:** all three are marked development packages, not release-ready SDKs, at version 3.3.5. Do not claim a registry release; not yet verified against a real server in CI or in every browser runtime.
- **Docs:** `src/README.md` (layout), `src/typescript/README.md`, `src/python/README.md`, `src/wasm/README.md`, and `docs/wiki/client-sdks.md` (comparison, quick start, verification status).
- **Do not describe the SDKs as:** a semantic runtime, a Foundgine server, an authorization layer, or an ORM/database client.

## Security

```text
untrusted input
      ↓
semantic resolution
      ↓
authorization
      ↓
security-preserving plan
      ↓
provider conformance
      ↓
execution
```

## Website path

- `/` — landing page and documentation map
- `/what-is-foundgine.html` — concept and boundary
- `/getting-started/` — first application
- `/walkthrough/` — one request, end to end
- `/architecture/` — canonical architecture
- `/ai-agents/` — agent boundary
- `/security/` — authorization and execution security
- `/samples/` — learning applications
- `/performance/` — benchmark methodology and evidence
- `/packages/` — package map, including the TypeScript, Python and WebAssembly client SDKs

## Current release

**3.3.5 · .NET 10 and Java 27**

The fluent configuration API was introduced in 2.3.0 and remains part of the current model API (`FoundgineModel`, with semantic, persistence, entity-map, connection and model-connection configuration classes) in both .NET and Java. It compiles to the same metadata registry as attributes/annotations and the AOT generator; see `docs/MAPPING.md`.

For implementation truth, use the active source tree, tests and package READMEs under `src/csharp/`, `src/java/`, `src/typescript/`, `src/python/` and `src/wasm/`.

## Deeper repository documentation

- [Documentation index](https://github.com/CristianBarragan/Foundgine/blob/main/docs/README.md)
- [Architecture](https://github.com/CristianBarragan/Foundgine/blob/main/docs/ARCHITECTURE.md)
- [Authorization](https://github.com/CristianBarragan/Foundgine/blob/main/docs/AUTHORIZATION.md)
- [Security](https://github.com/CristianBarragan/Foundgine/blob/main/docs/SECURITY.md)
- [AI agents](https://github.com/CristianBarragan/Foundgine/blob/main/docs/AI-AGENT.md)
- [Lexical grounding](https://github.com/CristianBarragan/Foundgine/blob/main/docs/LEXICAL-GROUNDING.md)
- [Grounding decisions](https://github.com/CristianBarragan/Foundgine/blob/main/docs/GROUNDING-DECISIONS.md)
- [Testing](https://github.com/CristianBarragan/Foundgine/blob/main/docs/TESTING.md)
