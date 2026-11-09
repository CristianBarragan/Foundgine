# Providers and infrastructure adapters

Providers are the implementation boundary between Foundgine's semantic execution contracts and physical infrastructure. They execute already-resolved, authorized work or provide bounded discovery evidence. They do **not** define the application's semantic truth, decide what a caller is allowed to do, or turn retrieval scores into permissions.

The .NET and Java provider modules are intentionally broad distribution units. A module can contain several independently selectable provider families; applications should configure only the implementations they need.

## Provider selection at a glance

| Provider / capability | Purpose | C# source | Java source |
|---|---|---|---|
| SQL execution | Compile semantic query plans to parameterized relational SQL and execute them | `src/csharp/Foundgine.Providers/Storage/Sql` | `src/java/foundgine-providers/.../storage/sql` |
| SQL mutations | Compile and execute mutation plans, including batched PostgreSQL paths | `Storage/Sql/Mutation` | `.../storage/sql/mutation` |
| In-memory execution | Deterministic, lightweight execution for tests, samples and controlled scenarios | `Storage/InMemory` | `.../storage/inmemory` |
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

**Source:** `src/csharp/Foundgine.Providers/Gateway` and `src/java/foundgine-providers/.../gateway`.

## MCP tools and client

**What it does.** Exposes Foundgine capabilities through Model Context Protocol tools and provides a client for discovering and invoking capabilities exposed by another MCP host.

**Server-side areas:** service registration, read/capability tools and mutation tools. **Client-side area:** `FoundgineMcpAgentClient` performs capability discovery and dynamic intent execution against a remote MCP server.

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

**Java.** The corresponding declarations and metadata helpers live under `com.foundgine.providers.aot`; generator support is in the Java AOT generator module. Verify the Maven module's published artifact and current processor configuration for the exact release.

**Use it when** compile-time metadata is preferred over runtime reflection/discovery, particularly for trimming/AOT-sensitive applications. Generated metadata describes the model; semantic configuration still determines meaning and authorization remains independent.

**Source:** `src/csharp/Foundgine.Providers/Aot`, `Foundgine.Providers.Aot.Generator`, `src/java/foundgine-providers/.../aot`, and the Java AOT generator module.

## AI agent and toolset descriptors

`FoundgineAiAgent` and `FoundgineAiToolset` are provider-side descriptors for agent/toolset configuration. They describe integration surfaces; they are not an LLM implementation, do not make semantic decisions, and do not confer authority. Agent-facing tools should call Foundgine's intent and mutation APIs rather than bypassing the runtime.

**Source:** `Models` in C# and `com.foundgine.providers.models` in Java.

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

- [Planning and execution](planning-execution.md)
- [Authorization and security](authorization-security.md)
- [Gateway, events and live updates](gateway-events.md)
- [AI agents and MCP](agents-mcp.md)
- [AOT and generated metadata](aot.md)
- [Cross-language implementation map](implementation-map.md)
