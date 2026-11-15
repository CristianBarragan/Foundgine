# Foundgine 3.3.5 (.NET & Java)

Foundgine is a **semantic execution layer for .NET 10 and Java 27**. It resolves structured caller intent into authorized, deterministic execution plans within an application-controlled boundary. Both implementations share the same architecture and vocabulary, expressed through language-idiomatic mechanisms.

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

Intent can originate from application code, JSON, GraphQL, MCP or AI tools. These are adapters/consumers of the semantic boundary. GraphQL adapters exist in both implementations (Hot Chocolate for .NET and GraphQL Java for Java). Microsoft.Extensions.AI is .NET-specific; a Microsoft Agent Framework adapter exists in the post-3.3.5 working tree but is not yet build-verified or released; JSON intent and MCP adapters exist in both .NET and Java.

Current providers include `Foundgine.Providers.Storage.Sql` (`foundgine-providers` in Java) for SQL/PostgreSQL and a deliberately limited InMemory provider, in both implementations. ORM execution is available through the EF Core adapter (.NET, `Foundgine.Providers.EntityFrameworkCore`) and the JPA adapter (Java, `storage/orm/jpa`) over a shared ORM-neutral core; maturity is tracked per capability. PostgreSQL retrieval includes relational lookup, `pg_trgm`, native full text, optional `pg_search`/BM25 and optional Apache AGE graph similarity, also in both implementations. PostgreSQL pgvector retrieval components exist in both implementations; production certification is a separate question.

## AI

AI may generate structured intent. It does not decide application authority, tenant identity, exposed semantics, credentials, policy or whether security invariants may be skipped.

![PlantUML diagram: llms-full, diagram 2](assets/llms-full-plantuml-02.svg)

## MCP

`Foundgine.Providers.Tools.MCP` (`com.foundgine.providers.tools.mcp` in Java) is a transport/capability adapter. Discovery is advisory. The host supplies trusted security context and every actual request is resolved and authorized.

## Security

The core security boundary is:

![PlantUML diagram: llms-full, diagram 3](assets/llms-full-plantuml-03.svg)

Identity, tenant, audience, secrets and other authority remain host-owned.

## Current evidence

The `Foundgine.SupplyChain` sample and AgentEndToEnd benchmark demonstrate an agent-facing business workload through MCP, semantic execution, authorization, ExecutionIR and PostgreSQL, in .NET. The associated PenTest suite contains 14 deterministic cases: 7 MCP and 7 GraphQL. Benchmark pages separate measured performance from modeled efficiency estimates.

The Java source tree (`src/java/samples/foundgine-supply-chain-advanced`) contains the MCP → semantic model → authorization → mutation engine → PostgreSQL path with claims-based authorization, high-assurance `place_order`/`cancel_order`, and adversarial invariants (recursive supplier-risk/BOM traversal, tenant isolation). The latest reported Maven run failed in the Advanced sample test phase; fixes are present but have not yet been rerun, so the sample remains implemented-unverified. The Java core implements `SemanticLexicalResolver.ground(...)`, alias-weight evidence and the `REQUIRES_CLARIFICATION` outcome. The Advanced sample also has a direct PostgreSQL candidate-search MCP tool named `find_top_supplier_overdue_orders`, but that tool is not wired through the core resolver. The AgentEndToEnd/PenTest benchmark harness has not yet been ported to Java.

## Packages

.NET (NuGet):

- `Foundgine` — runtime facade
- `Foundgine.Core.Abstractions` — stable contracts and identifiers
- `Foundgine.Core.Semantic.Metadata` — structural metadata
- `Foundgine.Core.Semantic` — meaning, intent, resolution, authorization
- `Foundgine.Core.Semantic.Planning` — provider-independent planning and safe rewrites
- `Foundgine.Core.Execution` — ExecutionIR, provider boundary, results and evidence
- `Foundgine.Providers.Storage.Sql` — SQL/PostgreSQL provider
- `Foundgine.Providers.Storage.InMemory` — limited non-SQL provider
- `Foundgine.Providers.Orm` / `Foundgine.Providers.EntityFrameworkCore` (.NET) and `storage/orm` / `storage/orm/jpa` (Java) — ORM execution core with EF Core and JPA adapters
- `Foundgine.Providers.Aot` / `Foundgine.Providers.Aot.Generator` — AOT declarations and source generation
- `Foundgine.Core.Serialization` — JSON intent adapter
- `Foundgine.Extensions.GraphQL.HotChocolate*` — GraphQL adapters/execution boundaries
- `Foundgine.Providers.Tools.MCP` — MCP adapter
- `Foundgine.Providers.Models` — `Microsoft.Extensions.AI` integration
- `Foundgine.Runtime.ControlPlane` — optional authority/recovery control-plane infrastructure

Java (Maven, groupId `io.github.cristianbarragan`):

- `foundgine-core` — provider-independent semantic model, metadata, intent, planning and execution contracts
- `foundgine-runtime` — application-facing orchestration, authorization and the mutation engine; its optional `controlplane` package contains tool registry/governance, approvals, audit, risk scoring and authorization-key rotation (not the full .NET authority-recovery feature set)
- `foundgine-providers` — SQL/PostgreSQL, InMemory, ORM/JPA, MCP, retrieval, gateway, event-store and outbox providers
- `foundgine-extensions` — optional GraphQL, Keycloak and Temporal adapters
- `foundgine-migrations` — Java schema/migration source module; Maven/runtime verification pending
- `foundgine-spring` and `foundgine-langchain4j` — optional integrations with source and tests present; dependency pins and runtime verification remain unverified in this snapshot
- `foundgine-aot-generator` — build-time annotation processor; included in the reactor but excluded from standalone publication

## Current release

**3.3.5 · .NET 10 and Java 27**

The fluent configuration API was introduced in 2.3.0 and remains part of the current model API (`FoundgineModel`, with semantic, persistence, entity-map, connection and model-connection configuration classes) in both .NET and Java. It compiles to the same metadata registry as attributes/annotations and the AOT generator; see `docs/MAPPING.md`.

For implementation truth, use the active source tree, tests, and package READMEs under `src/csharp/` and `src/java/`.

## Website navigation

- `/` — Foundgine landing page
- `/case-studies/supply-chain/` — featured Supply Chain case study with capabilities and benchmark evidence
- `/agent-benchmark/supply-chain/` — live/published Supply Chain E2E benchmark report
- `/samples/semantic/` — advanced semantic execution sample
- `/samples/pentest/` — security penetration-test sample
- `/architecture/` — architecture and lifecycle
- `/ai-agents/` — AI/agent boundary
- `/security/` — security model
- `/packages/` — package map
