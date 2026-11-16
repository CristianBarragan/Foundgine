# Foundgine documentation

This directory contains the technical documentation for the .NET and Java implementations and the supporting client SDKs. The two server implementations share the same architecture and vocabulary; pages call out language-specific differences where they matter. The source code, tests, package metadata and CI workflows are authoritative when documentation and implementation disagree.

The repository itself is the source of truth. Documentation describes implemented architecture and tested behavior; it does not use old release notes or historical benchmark snapshots as current product guidance.

## Read in order

This is a suggested reading path. Start at [Getting started](GETTING-STARTED.md) and follow the links that match your task.

1. [Getting started](GETTING-STARTED.md) — build the repository and run the main sample.
2. [Why Foundgine](WHY-FOUNDGINE.md) — the problem, and the boundary it draws.
3. [Product positioning and commercial strategy](PRODUCT-POSITIONING-AND-COMMERCIAL-STRATEGY.md) — target buyers, differentiation, business value and evidence requirements.
4. [What Foundgine is designed for](APPLICATION-CATEGORIES.md) — what, how, where it fits, and the categories of applications it targets.
5. [Architecture](ARCHITECTURE.md) — the intent-to-provider pipeline.
6. [Metadata → Semantics](METADATA-TO-SEMANTICS.md) — what exists versus what it means.
7. [Mapping and connections](MAPPING.md) — how the semantic model attaches to the physical EF Core or JPA model.
8. [Open Intent API](OPEN-INTENT-API.md) — typed, dynamic, and JSON ways to express intent.
9. [Lexical grounding](LEXICAL-GROUNDING.md) — resolving free-form language against the semantic contract.
10. [Grounding decisions](GROUNDING-DECISIONS.md) — telling a competing meaning apart from routing noise, and when Foundgine must ask instead of guess.
11. [Authorization](AUTHORIZATION.md) — what a caller may exercise, and under what conditions.
12. [Security](SECURITY.md) — the untrusted-input boundary and fail-closed rules.
13. [Runtime](RUNTIME.md) — how a request actually moves through resolution, planning, and execution.
14. [.NET AOT](AOT.md) — the .NET source-generator path; the [AOT overview](wiki/aot.md) compares .NET and Java.
15. [Public API](PUBLIC-API.md) — the shape of the day-to-day application surface.
16. [AI agents](AI-AGENT.md) — exposing capabilities to agents and MCP without granting database authority.
17. [PostgreSQL E2E](POSTGRES-E2E.md) — the real database integration path.
18. [Testing](TESTING.md) — how the boundaries above are proven, not just asserted.
19. [Source coverage](SOURCE-COVERAGE.md) — where implementation and test evidence live.
20. [Agent and framework integrations](wiki/agent-framework-integrations.md) — Microsoft Agent Framework, LangChain4j and Spring Boot.
21. [Client SDKs](wiki/client-sdks.md) — the TypeScript, Python and WebAssembly clients for a running server.

Package-specific architecture and usage guidance lives beside each implementation under `src/csharp/`, `src/java/`, `src/typescript/` (README), `src/python/` (README) and `src/wasm/` (README); [`src/README.md`](../src/README.md) maps them all. Sample tutorials live alongside their sample projects.
## Documentation rules

The documentation follows four rules:

1. **Current code wins.** If prose disagrees with source or tests, the source/tests are authoritative.
2. **Implemented and planned are separated.** A planned feature is not documented as a shipped capability.
3. **Transport and provider boundaries stay explicit.** GraphQL, MCP, JSON, AI, and SQL are adapters/providers around the semantic core.
4. **Historical material stays out of the active guide.** Release notes, old benchmark runs, and implementation diaries are not part of the current documentation set.

## Feature and capability wiki

The [Foundgine feature and capability wiki](wiki/index.md) explains Foundgine by capability rather than by release, with plain-language summaries at the top of each page. Good entry points:

- [Provider catalogue](wiki/providers.md): every provider by name (SQL, **EF Core**, **JPA**, in-memory, Elasticsearch, pgvector, PostgreSQL retrieval, event store, outbox, gateway, MCP, AI models).
- [ORM providers (EF Core and JPA)](wiki/orm-providers.md): run Foundgine plans through the ORM you already use, with an honest maturity table.
- [Fluent semantic reads (`SemanticQuery`)](wiki/semantic-query-builder.md): one read builder with the same members in C# and Java, with snippets for both.
- [Extensions](wiki/extensions.md): GraphQL, Keycloak and Temporal in one place.
- [Agent and framework integrations](wiki/agent-framework-integrations.md): Microsoft Agent Framework (.NET), LangChain4j and Spring Boot (Java).
- [Client SDKs](wiki/client-sdks.md): TypeScript (`@foundgine/typescript`), Python (`foundgine-sdk`) and WebAssembly (`@foundgine/wasm`) transport clients.
- [Insights, evidence and auditing](wiki/insights-evidence-auditing.md): receipts, audit, diagnostics and live events.
- [Glossary](wiki/glossary.md): short, friendly definitions.