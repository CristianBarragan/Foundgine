# Provider catalogue

> **In plain words.** A **provider** is the part that actually talks to the outside world for Foundgine: the database, the search engine, the event log, the message queue, the AI tool protocol. Foundgine decides *what* should happen and *whether it is allowed*; a provider only does the *how*. You pick the providers that match your stack and leave the rest out. Think of Foundgine as a driver who knows the rules and the destination, and providers as the vehicles that do the moving.
>
> **Using EF Core (.NET) or JPA (Java)?** You do not need to write SQL. See [ORM providers](orm-providers.md): Foundgine hands its plans to your existing ORM, and the permission rules are always added for you.

## Overview

Providers are the implementation boundary between Foundgine's semantic execution contracts and physical infrastructure. They execute already-resolved, authorized work or provide bounded discovery evidence. They do **not** define the application's semantic truth, decide what a caller is allowed to do, or turn retrieval scores into permissions.

The .NET and Java provider modules are intentionally broad distribution units. A module can contain several independently selectable provider families; applications should configure only the implementations they need.

## Every provider by name

| Family | Provider (main class) | Plain words | .NET | Java |
|---|---|---|---|---|
| **Run queries** | **SQL** (`SqlCompiler`, `SqlExecutionProvider`) | Writes safe, parameterised SQL itself | Yes | Yes |
| | **SQL mutations**, incl. PostgreSQL batched (`SqlMutationExecutionProvider`, `PostgresBatchedMutationExecutionProvider`) | Safe writes, many at once on PostgreSQL | Yes | Yes |
| | **In-memory** (`InMemoryProvider`) | Runs on plain objects in memory; for tests and demos | Yes | Yes |
| | **EF Core** (`Foundgine.Providers.EntityFrameworkCore`) | Lets your existing **Entity Framework Core** run the queries and writes | Yes | n/a |
| | **JPA** (`JpaOrmProvider`, `JpaOrmQuerySource`, `JpaOrmMutationSource`) | Lets your existing **Jakarta Persistence (JPA)** run the queries and writes | n/a | Yes (partial) |
| | **ORM core** (`Foundgine.Providers.Orm` / `storage/orm`) | The shared, ORM-neutral engine that EF Core and JPA plug into | Yes | Yes |
| **Find candidates** (grounding) | **Elasticsearch** (`ElasticsearchSemanticLexicalCandidateSource`) | Text search for "what did the caller mean?" | Yes | Yes |
| | **PostgreSQL + pgvector** (`PgVectorSemanticLexicalCandidateSource`) | Meaning-based (vector) search inside PostgreSQL | Yes | Yes |
| | **PostgreSQL retrieval** (`PostgresRetrievalCandidateSource`, `PostgresRetrievalOptions`) | Fuzzy and full-text matching with built-in PostgreSQL features | Yes | check module |
| **Remember and deliver** | **Event store** (`PostgresEventStore`, `PostgresGatewayEventStore`) | A durable history of events | Yes | Yes |
| | **Outbox** (`PostgresOutbox`, `FoundgineOutboxDispatcher`) | A to-do list of messages, sent after the data is saved | Yes | Yes |
| **Front door** | **Gateway** (`FoundgineGateway`) with provider registry, approval stores, event stores and HTTP/SSE routes | One stable entrance for other products; tracks providers | Yes | Yes |
| **AI** | **MCP tools and client** (`FoundgineMcpTools`, `FoundgineMcpMutationTools`, `FoundgineMcpAgentClient`) | Lets AI agents discover and call Foundgine safely | Yes | Yes |
| | **Models**, Microsoft.Extensions.AI (`FoundgineAiToolset`, `FoundgineAiAgent`) | Describes Foundgine as tools for an AI function-calling loop | Yes | `models` package |
| **Build time** | **AOT metadata generator** (`Foundgine.Providers.Aot.Generator`) | Writes your model description as ordinary code during the build | Yes | `foundgine-aot-generator` |
| | **ORM map generator** (`Foundgine.Providers.Orm.Generator`) | Writes the typed field maps EF Core needs | Yes | maps are provider-neutral today |
| **Declarations** | **Graph retrieval capability** (`GraphRetrieval`) | A label saying a provider supports graph lookups | Yes | Yes |

**Provider kinds the gateway understands** (`FoundgineProviderKind`): `EventStore`, `Outbox`, `Workflow`, `Messaging`, `Realtime`, `Persistence`, `Identity`, `Diagnostics`, `Metadata`. Temporal registers as a `Workflow` provider and Keycloak as an `Identity` provider (see [Extensions](extensions.md)).

**Which query provider should I pick?**

| Your situation | Pick |
|---|---|
| Plain relational database, you want the most control and cost estimation | **SQL** |
| You already use EF Core or JPA and want one data-access stack | **EF Core** or **JPA** ([details](orm-providers.md)) |
| Tests, samples, no database | **In-memory** |
| Not sure a feature is production-ready | Check the status tables in [ORM providers](orm-providers.md) and [Conformance and hardening](conformance-hardening.md) |

## Provider selection at a glance

| Provider / capability | Purpose | C# source | Java source |
|---|---|---|---|
| SQL execution | Compile semantic query plans to parameterized relational SQL and execute them | `src/csharp/Foundgine.Providers/Storage/Sql` | `src/java/foundgine-providers/.../storage/sql` |
| SQL mutations | Compile and execute mutation plans, including batched PostgreSQL paths | `Storage/Sql/Mutation` | `.../storage/sql/mutation` |
| In-memory execution | Deterministic, lightweight execution for tests, samples and controlled scenarios | `Storage/InMemory` | `.../storage/inmemory` |
| ORM execution core | Provider-neutral plan validation, authorization binding and execution over generated typed maps | `src/csharp/Foundgine.Providers.Orm` | `.../storage/orm` |
| EF Core provider | Executes plans and mutations through Entity Framework Core | `src/csharp/Foundgine.Providers.EntityFrameworkCore` | n/a |
| JPA provider | Executes plans and mutations through Jakarta Persistence | n/a | `.../storage/orm/jpa` |
| ORM map generator | Roslyn generator for typed entity maps | `Foundgine.Providers.Orm.Generator` | n/a |
| PostgreSQL retrieval | pg_trgm / full-text (optional pg_search, Apache AGE) candidate retrieval | `Storage/Sql/Retrieval` | check module |
| Elasticsearch retrieval | Lexical candidate retrieval backed by an Elasticsearch index | `Storage/Elasticsearch` | `.../storage/elasticsearch` |
| PostgreSQL + pgvector retrieval | Vector/semantic lexicon indexing and candidate retrieval using pgvector | `Storage/PostgresVector` | `.../storage/postgresvector` |
| PostgreSQL event store | Persist and read event envelopes | `EventStore` | `.../eventstore` |
| PostgreSQL outbox | Durable dispatch queue for integration events | `Outbox` | `.../outbox` |
| Gateway/provider registry | Transport-neutral capability facade, provider registration/negotiation, events, approvals and HTTP/SSE adapters | `Gateway` | `.../gateway` |
| MCP tools and client | Expose Foundgine capabilities to MCP clients and consume remote MCP capabilities | `Tools/MCP` | `.../tools/mcp` |
| AOT metadata | Compile-time declarations and generated semantic metadata | `Aot` + `Foundgine.Providers.Aot.Generator` | `.../aot` + `foundgine-aot-generator` |
| AI agent/toolset descriptors | Describe agent/toolset integration surfaces | `Models` | `.../models` |
| Graph retrieval capability | Declare graph-retrieval capability metadata for provider negotiation | `Hosting/Capabilities` | `.../capabilities` |

> The table is a source map, not a promise of identical implementation maturity or runtime dependencies. Verify the concrete language module, tests and package release before relying on a feature.

## SQL execution provider

**What it does.** SQL is the physical execution adapter for relational data. The compiler translates Foundgine's semantic query plan into a provider plan; the execution provider binds values and executes it through the database driver. Query construction must use trusted model mappings for identifiers and parameter binding for caller-controlled values.

**Key areas**
- `SqlCompiler` / `SqlPlan`: compile and represent the physical query plan.
- `SemanticQuerySqlWriter`: serialize semantic query operations into SQL.
- `SqlParameterBinding`: bind values separately from SQL structure.
- `SqlAuthorizationWriter` / `SqlAuthorizationPredicate`: render trusted authorization predicates.
- `SqlPaginationPlan`, `SqlCursorBinding` and `CursorCodec`: bounded paging and cursor handling.
- `SqlCostEstimator` / `SqlCostModelOptions`: estimate and constrain query cost.
- `SqlSecurityConformance`: shared security conformance helpers.

**Mutation execution.** The mutation subsystem has separate compiler and execution contracts (`SqlMutationCompiler`, `SqlMutationExecutionProvider`, mutation plans and return bindings). PostgreSQL-specific batched execution adds a batch boundary, correlation projection and batched compiler/executor. This is distinct from ordinary read execution and should be tested independently.

**Important constraints**
- Never interpolate caller values into SQL text.
- Resolve table/column identifiers from trusted semantic mappings, not request strings.
- Preserve tenant and row/field authorization predicates through compilation.
- Enforce limits for pagination, cost, result cardinality and batch size.
- Reject unsupported semantic operations rather than silently approximating them.
- Treat cursors as opaque, validated continuation state; bind them to the intended query/scope where supported.

**Source:** `src/csharp/Foundgine.Providers/Storage/Sql` and `src/java/foundgine-providers/src/main/java/com/foundgine/providers/storage/sql`.

## In-memory execution provider

**What it does.** Provides an in-process implementation of the execution contract over registered data sets. It is useful for unit/integration tests, examples, deterministic demonstrations and environments where a relational provider is not required.

**Implementation areas:** `InMemoryDataSet`, `InMemoryRow`, `InMemoryPlan`, `InMemoryCompiler` and `InMemoryExecutionProvider` (C# and Java equivalents).

**Use it when**
- testing semantic resolution and execution without a database;
- building a small runnable sample;
- validating provider-independent behavior or a test fixture.

**Do not treat it as** a production substitute for a durable database, a concurrency/transaction model, or a security boundary by itself. It must still receive the authorized plan and trusted execution scope. Tests should cover tenant isolation, unsupported operations, null/type behavior and limits.

## Elasticsearch retrieval provider

**What it does.** Implements lexical semantic-candidate retrieval over Elasticsearch. It is a retrieval source used during grounding, not the authority for semantic truth or permission.

**Implementation areas:** `SemanticLexiconIndexClient` and `ElasticsearchSemanticLexicalCandidateSource`.

**Operational guidance**
- Index a deliberate semantic projection, not unrestricted application records.
- Preserve stable semantic identifiers so retrieved candidates can be grounded against the compiled semantic model.
- Treat relevance scores as ranking evidence only; the resolver makes the semantic commitment and authorization remains a later, separate step.
- Define index refresh/rebuild procedures and stale-index behavior.
- Bound candidate counts, query time and payload size. Fail or return a documented incomplete result when a budget is exhausted.

**Source:** `Storage/Elasticsearch` in C# and `storage/elasticsearch` in Java.

## PostgreSQL / pgvector retrieval provider

**What it does.** Supplies vector-backed semantic lexicon indexing and candidate retrieval using PostgreSQL with pgvector. It complements lexical sources; it does not replace semantic validation or authorization.

**Implementation areas:** `PgVectorOptions`, `PgVectorDistance`, `PgVectorSemanticLexiconIndexClient` and `PgVectorSemanticLexicalCandidateSource`.

**Operational guidance**
- Keep the indexed projection limited to approved discovery metadata.
- Select and document the distance metric and index configuration.
- Version semantic documents and rebuild when the semantic contract changes.
- Bound candidate count, query time and vector dimensions; validate embedding compatibility.
- Never interpret vector similarity as permission to execute a capability.

**Source:** `Storage/PostgresVector` in C# and `storage/postgresvector` in Java.

## ORM providers: EF Core and JPA

**What they do.** Run Foundgine plans through the ORM you already have instead of generating SQL text. The shared ORM core validates the plan and binds it to the authorization decision; the adapter (EF Core for .NET, JPA for Java) turns the checked plan into an `IQueryable` (EF Core) or a Criteria query (JPA). Permission filters are always added and callers can only narrow results further.

**Be honest about maturity.** Much of this family is *implemented* but not yet *proven by a real database run*; aggregate cursor pagination is switched off until it is. The full status table, rules and setup are in **[ORM providers (EF Core and JPA)](orm-providers.md)**.

**Source:** `src/csharp/Foundgine.Providers.Orm`, `src/csharp/Foundgine.Providers.EntityFrameworkCore`, `src/csharp/Foundgine.Providers.Orm.Generator`; Java `foundgine-providers/.../storage/orm` and `.../orm/jpa`.

## PostgreSQL retrieval provider

**What it does.** `PostgresRetrievalCandidateSource` finds *possible matches* for a caller's words using PostgreSQL features: trigram similarity (`pg_trgm`, on by default), full-text search (on by default, English unless changed), and optionally `pg_search` and an Apache AGE graph (both off by default). Everything is switched with `PostgresRetrievalOptions`. Like the other retrieval sources it only produces candidates and evidence; authorization and the final query still belong to Foundgine.

**Source:** `Storage/Sql/Retrieval` (C#).

## Models: Microsoft.Extensions.AI

**What it does.** `FoundgineAiToolset` exposes Foundgine as AI tools (`foundgine_capabilities`, `foundgine_query`); `FoundgineAiAgent` runs a bounded function-calling loop over an existing Foundgine runtime. It does not hard-code OpenAI, Azure, Ollama or any model vendor. The model only supplies intent; the host supplies the security context and Foundgine still resolves, authorizes, plans and executes.

**Source:** `Models` (C#).

## Event store

**What it does.** Persists versioned event envelopes for audit-oriented history, replay and event-stream integration. The PostgreSQL implementation is separate from the gateway's in-process event hub and from the outbox.

**Implementation areas:** `IEventStore`, `EventEnvelope`, `EventStoreOptions`, `PostgresEventStore`, `PostgresGatewayEventStore`, and schema migration support.

**Design boundary**
- Event store = durable event history.
- Gateway event hub = live in-process delivery, typically bounded/best-effort.
- Outbox = reliable publication work associated with committed state changes.

Do not describe an in-memory gateway stream as a durable audit log. Define retention, payload versioning, integrity, migration and data-minimization policies for durable event storage. Avoid storing credentials or unnecessary personal data.

**Source:** `src/csharp/Foundgine.Providers/EventStore` and `src/java/foundgine-providers/.../eventstore`.

## Outbox provider and dispatcher

**What it does.** Stores dispatchable messages durably and leases them to a dispatcher for publication. The PostgreSQL implementation provides the storage side; `FoundgineOutboxDispatcher` handles bounded dispatch, retry/lease behavior and the publisher seam.

**Key contracts:** outbox message, lease, store/publisher contracts, dispatcher and dispatcher options.

**Delivery expectations**
- Design consumers for at-least-once delivery; use stable message identifiers and idempotent handlers.
- Make lease expiry, retry limits, backoff and dead-letter behavior operationally visible.
- Keep the transaction boundary between business state and outbox insertion explicit.
- Do not equate successful enqueue with successful downstream processing.

The Temporal extension can use the outbox publisher bridge, but workflow orchestration remains the responsibility of the Temporal extension.

**Source:** `src/csharp/Foundgine.Providers/Outbox` and `src/java/foundgine-providers/.../outbox`.

## Gateway and provider registry

**What it does.** Provides a transport-neutral facade for capability description, provider registration and negotiation, execution delegation, approvals, audit hooks, diagnostics and event publication. Optional HTTP/SSE adapters expose that facade to hosts.

**Major areas**
- `FoundgineGateway` and gateway contracts/options: facade and host configuration.
- Provider registry, descriptors, requirements, capabilities and negotiator: registration and compatibility negotiation.
- Execution facade: delegates reads and mutation lifecycle to the existing Foundgine runtime.
- Approvals: stores and validates approval receipts; approval is not a replacement for execution-time authorization.
- Audit hooks: allow durable audit acceptance to gate publication when configured as a critical hook.
- Events: bounded in-process event hub, subscriptions, cursors and visibility/scope filtering.
- HTTP: route mapping, request/response mapping, JSON serialization and SSE delivery.
- Diagnostics: activity and metric instrumentation.

**AOT and security notes (C#).** HTTP handlers and JSON metadata are designed for Native AOT; the gateway uses source-generated JSON metadata. Diagnostics intentionally avoid high-cardinality or sensitive labels. Protect semantic capability-description endpoints with the host's authentication/authorization policy because model topology and conditional predicates may be sensitive.

**Event delivery.** The in-process stream is bounded and best-effort. Slow subscribers may miss older events. Use an external broker through gateway hooks for cross-process delivery or durable replay. A critical audit sink should fail publication if durable acceptance is a requirement.

**Provider lifecycle.** The registry describes and tracks providers; it does not instantiate arbitrary providers, choose application execution policy, or grant caller authority. Validate provider requirements and health at startup.

**Insights and audit.** Events, execution status, diagnostics and the critical audit hook live here: see [Insights, evidence and auditing](insights-evidence-auditing.md).

**Source:** `src/csharp/Foundgine.Providers/Gateway` and `src/java/foundgine-providers/.../gateway`.

## MCP tools and client

**What it does.** Exposes Foundgine capabilities through Model Context Protocol tools and provides a client for discovering and invoking capabilities exposed by another MCP host.

**Server-side areas:** service registration, read/capability tools (`foundgine_capabilities`, `foundgine_query`, `foundgine_query_semantic`) and mutation tools (`foundgine_mutation_dry_run`, `foundgine_mutation_approve`, `foundgine_mutation_execute_approved`). **Client-side area:** `FoundgineMcpAgentClient` performs capability discovery and dynamic intent execution against a remote MCP server.

**Typical flow**
1. MCP client discovers available capabilities.
2. The client submits provider-neutral intent.
3. Foundgine resolves intent against the semantic model.
4. The runtime evaluates the current trusted security context and authorization.
5. The selected provider executes the authorized plan.

Discovery is descriptive, not a grant. Never trust a tool name, description, model-generated argument or remote MCP response as authorization evidence. The host must establish the trusted security execution context and Foundgine must authorize each operation.

**Source:** `src/csharp/Foundgine.Providers/Tools/MCP` and `src/java/foundgine-providers/.../tools/mcp`.

## AOT metadata and source generation

**What it does.** Supplies application-facing attributes and generated/runtime helpers for describing entities, fields, relationships, semantic models, connections/maps, conversions, aliases, authorization, dimensions and events.

**C# packaging.** The declarations/runtime support live in `Foundgine.Providers/Aot`. The Roslyn generator is a separate build-time project, `Foundgine.Providers.Aot.Generator`, but is packed as an analyzer inside the `Foundgine.Providers` NuGet package. A NuGet consumer should not need to install a separate generator package. The generator is not a runtime dependency.

**ORM maps.** A second Roslyn generator, `Foundgine.Providers.Orm.Generator`, reads the same `[FoundgineEntity]`, `[FoundgineField]` and `[FoundgineRelationship]` declarations and writes strongly typed maps for the EF Core provider, so no reflection or runtime expression compiling is needed. See [ORM providers](orm-providers.md).

**Java.** The corresponding declarations and metadata helpers live under `com.foundgine.providers.aot`; generator support is in the Java AOT generator module. Verify the Maven module's published artifact and current processor configuration for the exact release.

**Use it when** compile-time metadata is preferred over runtime reflection/discovery, particularly for trimming/AOT-sensitive applications. Generated metadata describes the model; semantic configuration still determines meaning and authorization remains independent.

**Source:** `src/csharp/Foundgine.Providers/Aot`, `Foundgine.Providers.Aot.Generator`, `src/java/foundgine-providers/.../aot`, and the Java AOT generator module.

## AI agent and toolset descriptors

See **Models: Microsoft.Extensions.AI** above. They are descriptors, not an LLM, and never confer authority. Source: `Models` in C# and `com.foundgine.providers.models` in Java.

## Graph retrieval capability

`GraphRetrieval` is a capability declaration used by provider negotiation to express graph-retrieval support. A capability declaration is descriptive metadata: it does not mean every provider implements graph traversal, nor does it authorize access to graph data. Confirm a concrete provider implementation and tests before depending on graph retrieval.

**Source:** `Hosting/Capabilities` in C# and `com.foundgine.providers.capabilities` in Java.

## Adding a provider

1. Identify the existing provider contract and the semantic/physical boundary it represents.
2. Declare capabilities and requirements accurately; do not advertise unsupported operations.
3. Accept a validated plan and trusted execution context, not raw caller-controlled physical identifiers.
4. Preserve tenant and authorization scope all the way to the data source.
5. Parameterize values and bound query, result, batch, time and memory costs.
6. Map provider errors without leaking secrets or database details.
7. Add conformance tests for types, nulls, cardinality, paging, authorization, tenant isolation, cancellation and failure paths.
8. Add startup validation, diagnostics, runnable samples and operational guidance.
9. Document C# and Java coverage separately; do not claim parity based only on matching package names.

## Related pages

- [ORM providers (EF Core and JPA)](orm-providers.md)
- [Provider overview and safety rules](providers-storage.md)
- [Insights, evidence and auditing](insights-evidence-auditing.md)
- [Extensions](extensions.md)
- [Planning and execution](planning-execution.md)
- [Authorization and security](authorization-security.md)
- [Gateway, events and live updates](gateway-events.md)
- [AI agents and MCP](agents-mcp.md)
- [AOT and generated metadata](aot.md)
- [Cross-language implementation map](implementation-map.md)
