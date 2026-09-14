# Foundgine — AI / Search Reference

## Canonical identity

**Current release: 2.0.1 (.NET) · 2.2.0 (Java)**

**Foundgine is a programmable semantic execution platform, implemented for .NET and Java.**

Primary category: **semantic execution for .NET and Java**. Foundgine establishes an application-controlled boundary between caller intent, semantic meaning, authorization, provider-independent planning, physical execution, and execution evidence. The two implementations share the same architecture and vocabulary — entities, fields, relationships, aliases, capabilities, planning, ExecutionIR, and evidence — expressed through language-idiomatic mechanisms (Roslyn source generation and attributes in .NET; annotation processing and `record`s in Java).

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
- Compile-time metadata declarations and code generation: Roslyn source generation and attributes in .NET; annotation processing (`@FoundgineEntity`/`@FoundgineRelationship`/`@FoundgineAlias`) in Java.
- JSON intent and MCP adapters in both implementations. GraphQL and `Microsoft.Extensions.AI` adapters are currently .NET-only.
- Optional control-plane infrastructure (authority/recovery, tool registry, approvals, audit log, risk scoring) in both implementations (`Foundgine.Runtime.ControlPlane` in .NET, `com.foundgine.runtime.controlplane` in Java).

## AI and agent boundary

AI is an **untrusted producer of structured intent**, not the authority.

![PlantUML diagram: ai.seo, diagram 2](assets/ai-seo-plantuml-02.svg)

The host owns identity, tenant, audience, credentials, model orchestration, secrets, policy and other trusted context. Model or transport arguments must not become security authority. Capability discovery is descriptive/advisory and is never an authorization grant.

## Current Supply Chain evidence

The repository contains a canonical `Foundgine.SupplyChain` (.NET) application and an AgentEndToEnd Supply Chain benchmark. The workload crosses Agent/MCP → Foundgine semantics → authorization → planner/ExecutionIR → Npgsql → PostgreSQL. It combines valid, invalid and unauthorized operations, high-assurance `PlaceOrder` behavior, ownership checks, server-side pricing, inventory validation, atomic mutation and replay protection.

The benchmark is accompanied by deterministic Supply Chain PenTest coverage: **7 MCP cases + 7 GraphQL cases = 14 security cases**. Published benchmark pages distinguish measured results from modeled efficiency estimates. Do not turn modeled reductions into measured claims.

The Java implementation carries the same shape under `src/java/samples`: `foundgine-supply-chain` is the domain/authorization starter, and `foundgine-supply-chain-advanced` wires the same claims-based authorization, high-assurance `place_order`/`cancel_order` mutation, a JSON-RPC MCP server, and a PostgreSQL-backed store end to end, plus deterministic adversarial scenarios (recursive supplier-risk/BOM traversal with cycle detection, tenant isolation). The ambiguity-resolution capability (`find_top_supplier_overdue_orders`) and the AgentEndToEnd/PenTest benchmark harness are not yet ported to Java.

## Current release / runtime

- Foundgine release: **2.0.1 (.NET)** · **2.2.0 (Java)**
- Target runtime: **.NET 9**, or **Java 21** via Maven (`io.github.cristianbarragan` artifacts)
- Primary physical provider: **PostgreSQL / SQL**, in both implementations
- InMemory provider: limited provider-independence proof/test implementation, in both implementations
- Vector retrieval: **not currently implemented** in either implementation; `pgvector` is not part of the current provider claim.
- `pg_search` and Apache AGE are **optional**, not always-on dependencies.
- GraphQL and `Microsoft.Extensions.AI` adapters are **.NET-only** at this time; Java exposes JSON intent and MCP adapters.

## What Foundgine is not

Do not describe Foundgine as a complete autonomous-agent platform, ORM replacement, database, GraphQL server, MCP server, identity provider, authorization server, workflow engine, or universal provider abstraction with complete feature parity — in either implementation.

## Documentation / source of truth

The active source code and tests are authoritative. Human documentation is under `docs/`; website documentation is under `docs-site/`. The canonical current-status reference is `docs/CURRENT-STATUS.md`. Package-specific READMEs live under `src/csharp/` and `src/java/`; sample tutorials live alongside each sample (`src/csharp/samples/*` and `src/java/samples/*`).

Historical milestone language should not be presented as current product capability. `Graphgine` and `CoffeeBeanery` are historical/prototype names and are not the current product identity.
