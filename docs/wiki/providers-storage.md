# Providers, storage and retrieval backends

## Purpose

Providers adapt semantic plans and capability contracts to concrete infrastructure. Foundgine core should not depend on a specific database, search engine, event store, HTTP server, or workflow vendor.

## Provider categories

### Query and execution providers

Translate validated plans into provider operations. SQL providers compile semantic filters/projections/orderings into parameterized SQL and enforce required authorization predicates. In-memory providers are useful for deterministic tests and simple deployments, but should preserve the same contract semantics.

### Retrieval providers

Provide candidate discovery for grounding. PostgreSQL pgvector and Elasticsearch integrations can index lexical/embedding projections and return candidate references. They are not authoritative semantic contracts and do not grant access.

### Event and outbox providers

Persist event envelopes and reliably dispatch notifications. Durable stores should define schema migration, retention, idempotency, and failure recovery.

### Gateway providers

Expose registered capabilities over an application boundary and negotiate compatible execution providers.

## SQL provider responsibilities

- Use trusted model mappings for identifiers.
- Parameterize caller values.
- Enforce tenant/authorization predicates.
- Respect type, null, aggregate, cardinality, and pagination semantics.
- Bound query cost and result size.
- Reject unsupported operations rather than approximating them unsafely.

## Vector/search provider responsibilities

- Index only the semantic projection intended for discovery.
- Preserve stable semantic identity in indexed documents.
- Treat scores as retrieval evidence, not permission.
- Enforce query budgets and truncation behavior.
- Handle index staleness and rebuilds; the compiled contract remains authoritative.

## Event store and outbox

An event store persists envelopes for audit, replay, or stream delivery. An outbox records dispatchable messages alongside business state changes. These components have different purposes: the event store is a durable event history abstraction; the outbox is a reliable publication mechanism.

## Adding a provider

1. Identify the core contract it implements.
2. Document supported operations and semantic limitations.
3. Implement parameterization, scope propagation, and error mapping.
4. Add conformance tests for types, nulls, cardinality, pagination, authorization, and failure cases.
5. Add resource limits and diagnostics.
6. Register and validate the provider at startup.
7. Add samples and operational documentation.

## Where to look in source

- C#: `src/csharp/Foundgine.Providers/Storage`, `EventStore`, `Outbox`, `Gateway`.
- Java: `src/java/foundgine-providers` and provider-specific subpackages.


## Full provider catalogue

For the component-by-component inventory, source paths, language coverage, security constraints and operational guidance, see the [Provider catalogue](providers.md).
