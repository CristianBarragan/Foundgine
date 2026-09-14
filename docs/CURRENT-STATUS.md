# Current status — Foundgine 2.2.1 (.NET & Java)

The repository ships two implementations of the same architecture, released together on a single aligned version line: the .NET implementation targets .NET 9, and the Java implementation targets Java 21 (Maven, groupId `io.github.cristianbarragan`). Except where noted below, everything on this page applies to both.

This page is intentionally short: it describes the current architectural state rather than preserving historical release notes.

## Implemented architecture

The active source tree contains the following layers:

![PlantUML diagram: CURRENT-STATUS, diagram 1](assets/current-status-plantuml-01.svg)

Around that core are:

```text
Metadata
AOT / source generation
JSON
GraphQL
MCP
AI
Security.Authority
```

## Current semantic capabilities

The semantic layer currently covers:

- semantic entities, fields, identities, relationships and aliases;
- typed and dynamic read intent;
- filters and logical filter composition;
- relationship filters and quantifiers;
- ordering and relationship-path ordering;
- limit/offset/cursor controls;
- semantic type/value validation;
- logical traversals;
- immutable semantic contract snapshots;
- semantic capability descriptions;
- entity/field/relationship authorization;
- read/write authorization;
- conditional authorization predicates;
- semantic mutation graphs;
- generated-value mutation dependencies;
- security execution context and warrant-related contracts.

## Current planning capabilities

The planner currently provides:

- provider-independent read plans;
- separate mutation planning;
- authorization-preserving plan state;
- execution IR lowering;
- deterministic plan/fingerprint concepts;
- conservative rewrite rules;
- authorization canonicalization;
- predicate pushdown;
- safe projection pruning;
- relationship traversal/join ordering metadata;
- aggregate-related rewrites and safety gates;
- provider-aware advisory cost estimation.

## Current execution capabilities

`Foundgine.Core.Execution` provides:

- provider compilation/execution contracts;
- execution IR;
- result materialization;
- execution evidence/receipts;
- provider security conformance;
- security-invariant execution gates;
- optional execution-time authorization revalidation;
- provider plan caching;
- mutation dependency/execution coordination.

## Current providers

### SQL

`Foundgine.Providers.Storage.Sql` (`foundgine-providers` / `com.foundgine.providers.storage.sql` in Java) provides the primary SQL implementation and PostgreSQL-specific functionality, including:

- parameterized SQL compilation;
- SQL execution through ADO.NET (.NET) or JDBC (Java);
- PostgreSQL retrieval candidate sources — relational, `Fuzzy` (`pg_trgm`), `FullText` (`tsvector`), optional `Search` (`pg_search`/BM25), and optional `GraphSimilarity` (Apache AGE);
- SQL security conformance;
- SQL mutation compilation;
- PostgreSQL batched mutation compilation/execution;
- provider cost estimation.

Both implementations carry `ParityTest`-style tests asserting the SQL provider behaves identically across languages (compilation, cursor authorization, execution evidence, batched mutation, storage-name quoting, upsert).

### InMemory

`Foundgine.Providers.Storage.InMemory` (`com.foundgine.providers.storage.inmemory` in Java) is a deliberately limited provider used to validate provider independence and support deterministic tests/examples, in both implementations.

## Current adapters

### GraphQL

Hot Chocolate adapters translate GraphQL into Foundgine semantic operations. Dedicated execution packages establish the secure host-owned execution boundary. **This adapter is .NET-only** — Java has no GraphQL adapter at this time.

### JSON

`Foundgine.Core.Serialization` (`com.foundgine.core.serialization` in Java) parses structured read intent with explicit complexity limits, in both implementations.

### MCP

`Foundgine.Providers.Tools.MCP` (`com.foundgine.providers.tools.mcp` in Java) exposes capability discovery, read intent, and optional mutation dry-run/approval/execution tools through MCP, in both implementations.

### AI

`Foundgine.Providers.Models` integrates with `Microsoft.Extensions.AI` and exposes Foundgine operations as model tools while keeping authority host-owned. **This adapter is .NET-only** — Java exposes the same capabilities through its JSON intent and MCP adapters instead.

## AOT

`Foundgine.Providers.Aot` provides compile-time declarations and runtime support, while `Foundgine.Providers.Aot.Generator` provides deterministic source-generated metadata via Roslyn. The former `Foundgine.Experimental` package has been removed entirely.

The Java implementation achieves the same compile-time-metadata outcome through annotation processing: `@FoundgineEntity`/`@FoundgineRelationship`/`@FoundgineAlias` annotations are processed by the `foundgine-aot-generator` module (also vendored under `foundgine-providers/aot`) to produce deterministic generated metadata, with parity tests asserting identical semantic-identity output to the .NET generator.

## Security architecture

The security model is based on these invariants:

![PlantUML diagram: CURRENT-STATUS, diagram 2](assets/current-status-plantuml-02.svg)

Capability discovery is advisory.

Authentication and identity lifecycle remain application/host responsibilities.

`Foundgine.Runtime.ControlPlane` (`com.foundgine.runtime.controlplane` in Java) is optional and outside the core, in both implementations.

## Java porting status

The core libraries (`foundgine-core`, `foundgine-runtime`, `foundgine-providers`, `foundgine-extensions`) and the `foundgine-redteam` security module are at parity with .NET, verified by matching `*ParityTest` suites. The `Foundgine.SupplyChain.Advanced` sample's OpenIntent-specific tests and MCP-penetration tests are also now at parity (`OpenIntentSupplyChainParityTest`, `OpenIntentMutationSecurityParityTest`, `AuthorizationMcpPenetrationParityTest`). The following are **not yet ported** to Java:

- the `Foundgine.SupplyChain.Advanced` sample's retrieval-strategy tests (fuzzy/full-text, `pg_search`/BM25, Apache AGE graph-similarity, and retrieval-capability selection);
- the `AgentEndToEnd` Supply Chain benchmark harness (the `SupplyChain.Layered` fixture and `Run1`–`Run5` orchestration) — only the `HighAssurance.Postgres`/`TransferFunds` banking fixture has a Java equivalent, under `benchmarks/foundgine-agent`;
- the standalone `security/pentest` module (the 14-case GraphQL+MCP PenTest harness).

The advanced sample's ambiguity-resolution capability (`find_top_supplier_overdue_orders`) has also been ported, as `TopSupplierOverdueOrdersService` hand-registered into `AdvancedMcpServer`, backed by new `suppliers`/`purchase_orders` tables in the sample's Postgres schema. Unlike the rest of that port, this one is **unverified against a live database** — there is no Postgres available in the environment it was written in, so it has not been run. It also knowingly simplifies one thing versus the C# original: the returned `plan` fingerprint is a stable hash of the fixed Supplier read shape rather than a real `SemanticPlan`/`ExecutionIRCompiler`-derived fingerprint, since wiring that whole chain for one demo capability was out of scope. Treat this port as a draft to verify, not as parity-tested.

## What is not claimed

Foundgine does not claim to be:

- a complete autonomous agent platform;
- a universal provider implementation;
- a replacement for ORMs for ordinary persistence;
- an authorization server;
- a workflow/orchestration engine.

Those are intentionally outside the core boundary.

## Source of truth

For implementation behavior use:

1. active source code (`src/csharp/` for .NET, `src/java/` for Java);
2. active tests;
3. package READMEs under `src/csharp/` and `src/java/`;
4. this documentation.

Historical release notes and benchmark snapshots have been removed from the active documentation set to avoid presenting old behavior as current.

---

Next: [Roadmap](ROADMAP.md)
