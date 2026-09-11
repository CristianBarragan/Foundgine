# Current status — Foundgine 2.0.1

The repository is on the 2.0.1 release line and targets .NET 9.

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

`Foundgine.Providers.Storage.Sql` provides the primary SQL implementation and PostgreSQL-specific functionality, including:

- parameterized SQL compilation;
- SQL execution through ADO.NET;
- PostgreSQL retrieval candidate sources — relational, `Fuzzy` (`pg_trgm`), `FullText` (`tsvector`), optional `Search` (`pg_search`/BM25), and optional `GraphSimilarity` (Apache AGE);
- SQL security conformance;
- SQL mutation compilation;
- PostgreSQL batched mutation compilation/execution;
- provider cost estimation.

### InMemory

`Foundgine.Providers.Storage.InMemory` is a deliberately limited provider used to validate provider independence and support deterministic src/csharp/tests/examples.

## Current adapters

### GraphQL

Hot Chocolate adapters translate GraphQL into Foundgine semantic operations. Dedicated execution packages establish the secure host-owned execution boundary.

### JSON

`Foundgine.Core.Serialization` parses structured read intent with explicit complexity limits.

### MCP

`Foundgine.Providers.Tools.MCP` exposes capability discovery, read intent, and optional mutation dry-run/approval/execution tools through MCP.

### AI

`Foundgine.Providers.Models` integrates with `Microsoft.Extensions.AI` and exposes Foundgine operations as model tools while keeping authority host-owned.

## AOT

`Foundgine.Providers.Aot` provides compile-time declarations and runtime support, while `Foundgine.Providers.Aot.Generator` provides deterministic source-generated metadata. The former `Foundgine.Experimental` package has been removed entirely.

## Security architecture

The security model is based on these invariants:

![PlantUML diagram: CURRENT-STATUS, diagram 2](assets/current-status-plantuml-02.svg)

Capability discovery is advisory.

Authentication and identity lifecycle remain application/host responsibilities.

`Foundgine.Runtime.ControlPlane` is optional and outside the core.

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

1. active source code;
2. active tests;
3. package READMEs under `src/`;
4. this documentation.

Historical release notes and benchmark snapshots have been removed from the active documentation set to avoid presenting old behavior as current.

---

Next: [Roadmap](ROADMAP.md)
