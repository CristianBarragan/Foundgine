# Foundgine — AI / Search Reference

## Canonical identity

**Current release: 3.4.0 (.NET & Java)**

**Foundgine is a programmable semantic execution platform, implemented for .NET and Java.**

Primary category: **semantic execution for .NET and Java**. Foundgine establishes an application-controlled boundary between caller intent, semantic meaning, authorization, provider-independent planning, physical execution, and execution evidence. The two implementations share the same architecture and vocabulary — entities, fields, relationships, aliases, capabilities, planning, ExecutionIR, and evidence — expressed through language-idiomatic mechanisms (Roslyn source generation and attributes in .NET; annotation processing and `record`s in Java).

Alongside the two server implementations, the repository ships three thin **client SDKs** for calling a running Foundgine server: TypeScript (`@foundgine/typescript`, `src/typescript/`), Python (`foundgine-sdk`, `src/python/`) and Rust/WebAssembly (`@foundgine/wasm`, `src/wasm/`). They are transport clients, not a third implementation of the semantic runtime.

Do not reduce Foundgine to GraphQL, SQL, an ORM, an AI framework, an MCP implementation, RAG, or an autonomous-agent runtime.

## Canonical lifecycle

![PlantUML diagram: ai.seo, diagram 1](assets/ai-seo-plantuml-01.svg)

**Critical invariant:** retrieval discovers candidates and evidence; it does not grant authority. Authorization happens after semantic resolution and remains bound through planning and execution.

## What Foundgine provides

- Semantic entities, fields, identities, relationships, aliases and capabilities.
- Typed and dynamic intent.
- Semantic resolution, validation and graph-constrained traversal.
- Lexical candidate retrieval across semantic kinds.
- PostgreSQL-backed retrieval strategies: relational lookup, `pg_trgm` fuzzy search, native `tsvector` full text, optional `pg_search`/BM25, and optional Apache AGE graph similarity.
- Provider-independent query and mutation planning.
- Security-preserving plan rewrites and authorization binding.
- ExecutionIR, provider conformance checks, execution-time authorization revalidation, evidence/receipts and plan fingerprints.
- SQL/PostgreSQL and deliberately limited InMemory providers, in both implementations.
- Fluent model configuration (introduced in 2.3.0): `FoundgineModel` plus per-concern configuration classes in .NET and Java, compiled to the same metadata registry as the declarations below.
- Compile-time metadata declarations and code generation: Roslyn source generation and attributes in .NET; annotation processing (`@FoundgineEntity`/`@FoundgineRelationship`/`@FoundgineAlias`) in Java.
- JSON intent and MCP adapters in both implementations. GraphQL adapters exist in both implementations (Hot Chocolate for .NET and GraphQL Java for Java); Microsoft.Extensions.AI is .NET-specific. A Microsoft Agent Framework adapter is included in the 3.4.0 source, but it has not yet been compiled or tested and its package publication is not verified.
- The .NET `Foundgine.Runtime.ControlPlane` module provides the optional authority-recovery surface (witness quorum, credential lifecycle, journal reconciliation and promotion/failover). Java has a separate `com.foundgine.runtime.controlplane` package for tool registry/governance, approvals, audit, risk scoring and authorization-key rotation; it is not feature-equivalent to the .NET recovery module.

## Agent framework and Spring integrations

- **Microsoft Agent Framework (.NET):** `Foundgine.Providers` — Microsoft.Extensions.AI tools, a MAF agent factory and DI registration; writes off by default and human-approved.
- **LangChain4j (Java):** `foundgine-providers` (LangChain4j adapter) — `FoundgineLangChain4jTools.toolMap(server)` exposes Foundgine MCP tools to LangChain4j `AiServices`.
- **Spring Boot (Java):** `foundgine-providers` (Spring Boot adapter) — auto-configuration for the MCP server/controller (`foundgine.mcp.*`, off by default) and the JPA provider.
- All three keep identity host-owned and add no authority; source and tests are present but they are **not yet build- or runtime-verified**. Do not describe them as released or verified.
- Details: `docs/wiki/agent-framework-integrations.md` and each module's `README.md`.

## Client SDKs (TypeScript, Python, WebAssembly)

| SDK | Directory | Package | Runtime |
|---|---|---|---|
| TypeScript | `src/typescript/` | `@foundgine/typescript` (npm) | Node.js 20+ or any runtime with `fetch` |
| Python | `src/python/` | `foundgine-sdk` (PyPI name) | Python 3.11+, `httpx`; async and sync clients |
| Rust / WebAssembly | `src/wasm/` | `@foundgine/wasm` (npm) | Node.js 20+, browsers, Workers, Deno; host supplies `fetch` |

- They speak MCP over Streamable HTTP (default path `/mcp`, protocol version `2025-06-18`) and the Temporal workflow routes under `/foundgine/workflows`.
- Server tools they call: `foundgine_capabilities`, `foundgine_query`, `foundgine_query_semantic`, `foundgine_mutation_dry_run`, `foundgine_mutation_approve`, `foundgine_mutation_execute_approved`.
- They carry no authority: identity, tenant, authorization, planning and providers remain on the .NET or Java server, and capability discovery is advisory.
- Status: version 3.4.0, marked development packages (not release-ready SDKs); not yet verified against a real server in CI. Do not claim a registry release.
- Details: `src/README.md`, each SDK's `README.md`, and `docs/wiki/client-sdks.md`.

## AI and agent boundary

AI is an **untrusted producer of structured intent**, not the authority.

![PlantUML diagram: ai.seo, diagram 2](assets/ai-seo-plantuml-02.svg)

The host owns identity, tenant, audience, credentials, model orchestration, secrets, policy and other trusted context. Model or transport arguments must not become security authority. Capability discovery is descriptive/advisory and is never an authorization grant.

## Current Supply Chain evidence

The repository contains a canonical `Foundgine.SupplyChain` (.NET) application and an AgentEndToEnd Supply Chain benchmark. The workload crosses Agent/MCP → Foundgine semantics → authorization → planner/ExecutionIR → Npgsql → PostgreSQL. It combines valid, invalid and unauthorized operations, high-assurance `PlaceOrder` behavior, ownership checks, server-side pricing, inventory validation, atomic mutation and replay protection.

The benchmark is accompanied by deterministic Supply Chain PenTest coverage: **7 MCP cases + 7 GraphQL cases = 14 security cases**. Published benchmark pages distinguish measured results from modeled efficiency estimates. Do not turn modeled reductions into measured claims.

The Java source tree under `src/java/samples` contains a domain/authorization starter and an Advanced sample with claims-based authorization, high-assurance `place_order`/`cancel_order` mutation, a JSON-RPC MCP server, a PostgreSQL-backed store and adversarial scenarios (recursive supplier-risk/BOM traversal with cycle detection, tenant isolation). The latest reported Maven run failed in the Advanced sample test phase; fixes are present but have not yet been rerun, so this is implemented-unverified rather than a verified end-to-end result. The Java core implements `SemanticLexicalResolver.ground(...)`, alias-weight evidence and the `REQUIRES_CLARIFICATION` outcome. The Advanced sample also has a direct PostgreSQL candidate-search MCP tool named `find_top_supplier_overdue_orders`, but that tool is not wired through the core resolver. The AgentEndToEnd/PenTest benchmark harness is not yet ported to Java.

## Current release / runtime

- Foundgine release: **3.4.0**, aligned across .NET and Java
- Target runtime: **.NET 10**, or **Java 27** via Maven (`io.github.cristianbarragan` artifacts)
- Client SDKs: **3.4.0** TypeScript, Python and Rust/WebAssembly transport clients (development packages), version-aligned with the servers
- Primary physical provider: **PostgreSQL / SQL**, in both implementations
- InMemory provider: limited provider-independence proof/test implementation, in both implementations
- PostgreSQL pgvector retrieval components are present in both implementations; their presence does not by itself establish production certification.
- `pg_search` and Apache AGE are **optional**, not always-on dependencies.
- GraphQL adapters exist in both implementations (Hot Chocolate for .NET and GraphQL Java for Java); Microsoft.Extensions.AI is .NET-specific. A Microsoft Agent Framework adapter is included in the 3.4.0 source, but it has not yet been compiled or tested and its package publication is not verified.

## What Foundgine is not

Do not describe the TypeScript, Python or WebAssembly SDKs as the semantic runtime, a server, an authorization layer or a database client. Do not describe Foundgine as a complete autonomous-agent platform, ORM replacement, database, GraphQL server, MCP server, identity provider, authorization server, workflow engine, or universal provider abstraction with complete feature parity — in either implementation.

## Documentation / source of truth

The active source code and tests are authoritative. Human documentation is under `docs/`; website documentation is under `docs-site/`. Package-specific READMEs live under `src/csharp/`, `src/java/`, `src/typescript/`, `src/python/` and `src/wasm/`; sample tutorials live alongside each sample (`src/csharp/samples/*` and `src/java/samples/*`).

Historical milestone language should not be presented as current product capability. `Graphgine` and `CoffeeBeanery` are historical/prototype names and are not the current product identity.
