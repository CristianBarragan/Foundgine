# Foundgine 3.4.0 — Website full technical context

Foundgine is a configurable semantic and execution boundary for application capabilities. It separates application meaning, persistence mapping, authorization and execution from the interfaces and agents that consume them.

Applications configure their capabilities using language-idiomatic fluent APIs. Foundgine resolves operations against configured semantics, applies authorization at the execution boundary, and carries the authorized operation into provider execution. The same configured model can serve multiple interfaces and consumers.

Foundgine is designed for conventional application development first. It does not require AI, MCP, GraphQL or an agent framework. Those are optional consumers and integrations. It complements existing application architectures, ORM technologies and databases rather than claiming to replace them.

## Product position and adoption

Foundgine is not an AI framework and does not require AI. It is intended to be useful for application capabilities and conventional interfaces first. Fluent configuration is the primary experience in both C# and Java. Existing persistence and domain infrastructure can remain in place; adoption should be incremental and capability-specific.

It complements conventional API frameworks, ORMs, databases and identity systems. It does not claim universal replacement, universal performance superiority or inherent security superiority. Security properties depend on application configuration, trusted context, policy, provider implementation and verification.

Foundgine is a **semantic execution layer for .NET 10 and Java 27**. It resolves structured caller intent into authorized, deterministic execution plans within an application-controlled boundary. Both implementations share the same architecture and vocabulary, expressed through language-idiomatic mechanisms. Thin TypeScript, Python and WebAssembly client SDKs for a running server are included under `src/typescript/`, `src/python/` and `src/wasm/` (see *Client SDKs* below). Optional Microsoft Agent Framework (.NET), LangChain4j and Spring Boot (Java) integrations are documented in *Agent framework and Spring integrations*.

## Canonical lifecycle

![PlantUML diagram: llms-full, diagram 1](assets/llms-full-plantuml-01.svg)

Retrieval is candidate discovery, not authorization.

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

- **Semantic model** — application-defined meaning: entities, fields, identities, relationships, aliases and capabilities.
- **Intent** — what the caller requests, independently of physical SQL/provider operations.
- **Retrieval** — candidate discovery plus evidence; never an authorization decision.
- **Resolution** — selects and validates the intended semantic meaning.
- **Authorization** — determines what the current trusted execution context may exercise.
- **Plan** — provider-independent logical execution.
- **ExecutionIR** — the controlled intermediate execution representation at the provider boundary.
- **Provider** — physical execution implementation.
- **Evidence** — execution/security/plan context such as receipts and fingerprints.

## Adapters and providers

Intent can originate from application code, JSON, GraphQL, MCP or AI tools. These are adapters/consumers of the semantic boundary. GraphQL adapters exist in both implementations (Hot Chocolate for .NET and GraphQL Java for Java). Microsoft.Extensions.AI and the Microsoft Agent Framework adapter are included in the .NET provider package; the adapter source project and test project are present in the solution, but this audit did not run the .NET build or tests. JSON intent and MCP adapters exist in both .NET and Java.

Current providers include `Foundgine.Providers.Storage.Sql` (`foundgine-providers` in Java) for SQL/PostgreSQL and a deliberately limited InMemory provider, in both implementations. ORM execution is available through the EF Core adapter (.NET, `Foundgine.Providers.EntityFrameworkCore`) and the JPA adapter (Java, `storage/orm/jpa`) over a shared ORM-neutral core; maturity is tracked per capability. PostgreSQL retrieval includes relational lookup, `pg_trgm`, native full text, optional `pg_search`/BM25 and optional Apache AGE graph similarity, also in both implementations. PostgreSQL pgvector retrieval components exist in both implementations; production certification is a separate question.

## AI

AI may generate structured intent. It does not decide application authority, tenant identity, exposed semantics, credentials, policy or whether security invariants may be skipped.

![PlantUML diagram: llms-full, diagram 2](assets/llms-full-plantuml-02.svg)

## MCP

`Foundgine.Providers.Tools.MCP` (`com.foundgine.providers.tools.mcp` in Java) is a transport/capability adapter. Discovery is advisory. The host supplies trusted security context and every actual request is resolved and authorized.

## Agent framework and Spring integrations (MAF, LangChain4j, Spring Boot)

Optional adapters that put Foundgine behind agent frameworks or host it in Spring. The framework handles conversation or hosting; Foundgine still resolves, authorizes and executes, and the host supplies identity (`ISecurityExecutionContextProvider`), never the model.

| Integration | Language | Module / path | Provides |
|---|---|---|---|
| Microsoft Agent Framework (MAF) | .NET | `Foundgine.Providers` package (bundled adapter assembly; source project `src/csharp/Foundgine.Providers.AgentFramework/`) | `AddFoundgineAgentFramework()`, `FoundgineAgentTools`, `FoundgineAgentFactory`; mutation tools are opt-in and approval-gated |
| LangChain4j | Java | `foundgine-providers` (`src/java/foundgine-providers/.../langchain4j/`) | `FoundgineLangChain4jTools.toolMap(FoundgineMcpServer)` returning LangChain4j `ToolSpecification`/`ToolExecutor` pairs for `AiServices`; optional framework dependencies |
| Spring Boot | Java | `foundgine-providers` (`src/java/foundgine-providers/.../spring/`) | Auto-configuration for MCP hosting/controller and JPA integration; host configuration and authentication required |

- **Package layout:** the .NET MAF adapter is a separate non-packable source project whose assembly is bundled by the `Foundgine.Providers` packaging project. The Java provider module contains Spring and LangChain4j integration source and declares their dependencies. Separate `foundgine-spring` and `foundgine-langchain4j` directories exist but are not included in the parent Maven reactor; do not infer their standalone publication from source presence.
- **Verification:** this archive has not been built in the current audit environment. Source and test files are present; build, runtime and publication status must be verified independently.
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
- **Status:** all three are marked development packages, not release-ready SDKs, at version 3.4.0. Do not claim a registry release; not yet verified against a real server in CI or in every browser runtime.
- **Docs:** `src/README.md` (layout), `src/typescript/README.md`, `src/python/README.md`, `src/wasm/README.md`, and `docs/wiki/client-sdks.md` (comparison, quick start, verification status).
- **Do not describe the SDKs as:** a semantic runtime, a Foundgine server, an authorization layer, or an ORM/database client.

## Security

The core security boundary is:

![PlantUML diagram: llms-full, diagram 3](assets/llms-full-plantuml-03.svg)

Identity, tenant, audience, secrets and other authority remain host-owned.

## Current evidence

The `Foundgine.SupplyChain` sample and AgentEndToEnd benchmark demonstrate an agent-facing business workload through MCP, semantic execution, authorization, ExecutionIR and PostgreSQL, in .NET. The associated PenTest suite contains 14 deterministic cases: 7 MCP and 7 GraphQL. Benchmark pages separate measured performance from modeled efficiency estimates.

The Java source tree (`src/java/samples/foundgine-supply-chain-advanced`) contains the MCP → semantic model → authorization → mutation engine → PostgreSQL path with claims-based authorization, high-assurance `place_order`/`cancel_order`, and adversarial invariants (recursive supplier-risk/BOM traversal, tenant isolation). The repository changelog reports a prior Maven failure in the Advanced sample test phase and says fixes had not yet been rerun at that checkpoint; this audit did not run Maven, so current build status remains unverified. The Java core implements `SemanticLexicalResolver.ground(...)`, alias-weight evidence and the `REQUIRES_CLARIFICATION` outcome. The Advanced sample also has a direct PostgreSQL candidate-search MCP tool named `find_top_supplier_overdue_orders`, but that tool is not wired through the core resolver. The AgentEndToEnd/PenTest benchmark harness has not yet been ported to Java.

## Packages and modules

The release/changelog describes four primary package artifacts per server implementation. Source projects, assemblies, namespaces and Maven reactor modules are not necessarily separately published packages. Registry publication was not checked in this audit.

### .NET / NuGet

- `Foundgine.Core` — core contracts, metadata and semantic model.
- `Foundgine.Runtime` — application runtime, authorization and orchestration.
- `Foundgine.Providers` — bundled provider assemblies, including SQL/in-memory, ORM and EF Core, Redis, Microsoft Agent Framework, pgvector, Elasticsearch, MCP/model integrations and generators.
- `Foundgine.Extensions` — bundled GraphQL, Temporal and Keycloak extension assemblies.

Packaging projects bundle assemblies from non-packable source projects; do not present every source project or assembly name as an independently published NuGet package.

### Java / Maven

- `foundgine-core` — provider-independent semantic model, metadata, intent, planning and execution contracts.
- `foundgine-runtime` — application orchestration, authorization and mutation execution.
- `foundgine-providers` — SQL/PostgreSQL, InMemory, ORM/JPA, MCP, retrieval, gateway, event-store, outbox, Redis/shared-state, Spring Boot and LangChain4j integration source.
- `foundgine-extensions` — GraphQL, Keycloak and Temporal extensions.

The source reactor also contains `foundgine-migrations` and `foundgine-aot-generator`. The AOT generator is build-time tooling and is not described as a standalone published runtime. Migration-module verification and publication status require separate evidence. Standalone `foundgine-spring` and `foundgine-langchain4j` directories exist, but the parent reactor comments describe those integrations as compiled into `foundgine-providers`; the standalone directories are not reactor modules.

### Internal project / assembly names

- .NET: `Foundgine.Providers.Orm`, `Foundgine.Providers.EntityFrameworkCore`, `Foundgine.Providers.Redis`, `Foundgine.Providers.AgentFramework`, `Foundgine.Providers.PostgresVector`, `Foundgine.Providers.Elasticsearch`, `Foundgine.Providers.Aot.Generator`, `Foundgine.Providers.Orm.Generator`, and extension project names are source/build components bundled by packaging projects.
- Java: source package names and reactor modules may not correspond one-to-one with published artifacts. Verify Maven publication metadata before presenting a module as a separately installable release.

## Current release

**3.4.0 · .NET 10 and Java 27**

The fluent configuration API was introduced in 2.3.0 and remains part of the current model API (`FoundgineModel`, with semantic, persistence, entity-map, connection and model-connection configuration classes) in both .NET and Java. It compiles to the same metadata registry as attributes/annotations and the AOT generator; see `docs/MAPPING.md`.

For implementation truth, use the active source tree, tests, and package READMEs under `src/csharp/`, `src/java/`, `src/typescript/`, `src/python/` and `src/wasm/`. **This documentation snapshot has not been built or test-run as part of the documentation audit; source presence is not verification of build, runtime or publication status.**

## Website navigation

The repository website is published under the GitHub Pages project path. Current source pages include:

- `/` — landing page
- `/what-is-foundgine.html` — product definition and boundary
- `/getting-started/` — getting started
- `/walkthrough/` — walkthrough
- `/how-it-works/` — execution lifecycle
- `/architecture/` — architecture
- `/ai-agents/` — AI and agent boundary
- `/security/` — security model
- `/samples/semantic/` and `/samples/pentest/` — samples
- `/case-studies/supply-chain/` — case study
- `/agent-benchmark/supply-chain/` — benchmark report
- `/packages/` — package map
- `/performance/` — performance material

## Website paths

- `https://foundgine.github.io/Foundgine/docs-site/` — landing page
- `https://foundgine.github.io/Foundgine/docs-site/what-is-foundgine.html` — product definition
- `https://foundgine.github.io/Foundgine/docs-site/getting-started/` — getting started
- `https://foundgine.github.io/Foundgine/docs-site/walkthrough/` — walkthrough
- `https://foundgine.github.io/Foundgine/docs-site/architecture/` — architecture
- `https://foundgine.github.io/Foundgine/docs-site/ai-agents/` — AI/agent boundary
- `https://foundgine.github.io/Foundgine/docs-site/security/` — security
- `https://foundgine.github.io/Foundgine/docs-site/samples/` — samples
- `https://foundgine.github.io/Foundgine/docs-site/case-studies/supply-chain/` — case study
- `https://foundgine.github.io/Foundgine/docs-site/agent-benchmark/supply-chain/` — benchmark report
- `https://foundgine.github.io/Foundgine/docs-site/packages/` — package map
- `https://foundgine.github.io/Foundgine/docs-site/performance/` — performance material
