# Architecture

Foundgine is a semantic execution layer between application intent and physical execution.

The central architectural rule is:

> **Callers describe intent. The semantic model defines meaning. Authorization determines authority. Planning defines logical execution. Providers define physical execution.**

## Canonical pipeline

<p align="center"><img src="assets/canonical-architecture.svg" alt="Foundgine canonical architecture showing the complete semantic lifecycle and parallel retrieval strategies." width="100%"></p>

The canonical lifecycle is: **Caller → Intent → Semantic Model → Semantic Operation Graph → Retrieval → Resolution → Authorization → Plan Binding → Execution IR → Provider → Execution → Evidence**. Other pages may focus on individual stages, but they must preserve this ordering.

The fully qualified type names below use .NET naming unless a Java name is shown. The corresponding Java implementation lives mainly in `foundgine-core`, `foundgine-runtime`, `foundgine-providers` and `foundgine-extensions`, under `com.foundgine.*` packages. The two APIs share the same architectural boundaries, but their type names and integration surfaces are not identical.

**Diagram source note:** the SVGs under `docs/assets/` are preserved rendered references. Their PlantUML source files are not present in this snapshot; do not hand-edit the SVG output.

![PlantUML diagram: ARCHITECTURE, diagram 1](assets/architecture-plantuml-01.svg)

## Semantic Operation Graph → Authorization → Plan Binding → Execution

The semantic operation graph is the canonical security object for a resolved request. It makes the requested topology explicit before planning and gives authorization one complete object to evaluate rather than a collection of transport-specific fragments.

The lifecycle is deliberately monotonic:

![PlantUML diagram: ARCHITECTURE, diagram 2](assets/architecture-plantuml-02.svg)

### The graph is the security unit

`SemanticOperationGraph` represents the complete resolved operation topology: root and child nodes, fields, relationships/connections and semantic query constraints. Graph validation and resource limits run before expensive planning or provider work.

Authorization evaluates the complete graph against the trusted immutable `SemanticContractSnapshot`. For graph authorization, the provider is absent from the decision boundary. A successful decision produces both an authorized graph and immutable authorization evidence.

![PlantUML diagram: ARCHITECTURE, diagram 3](assets/architecture-plantuml-03.svg)

Retrieval strategies such as relational lookup, fuzzy search, full-text search, BM25 or Apache AGE may help resolve ambiguous references, but they only produce candidates and evidence. They never become the authority over which graph nodes may be exercised.

![PlantUML diagram: ARCHITECTURE, diagram 4](assets/architecture-plantuml-04.svg)

### Plan binding is provenance, not a second authorization system

When an authorized operation is planned, the resulting `SemanticPlan` carries a `SemanticPlanAuthorizationBinding`. The binding records the fingerprints of the exact semantic contract and authorization decision that produced the plan.

![PlantUML diagram: ARCHITECTURE, diagram 5](assets/architecture-plantuml-05.svg)

Planner rewrites are required to preserve this binding. A rewrite that adds, removes, or changes the authorization provenance is rejected rather than silently producing an authorization-free plan.

This means optimization can change execution shape without changing the authority under which that shape was created.

### ExecutionIR is the next trust boundary

`ExecutionIR` is produced only from a plan carrying authorization provenance. Before it is accepted for provider compilation, the binding can be checked against the same semantic contract and authorization evidence.

The provider plan then inherits the same binding. The final execution gate additionally requires a provider security proof bound to the exact provider plan and exact `ExecutionIR`.

![PlantUML diagram: ARCHITECTURE, diagram 6](assets/architecture-plantuml-06.svg)

The important invariant is:

> **An executable provider artifact must remain traceably bound to the semantic contract and authorization decision that produced it.**

Changing the contract, authorization evidence, execution IR, provider, or security proof breaks that chain and causes execution to fail closed.

### Reads and mutations share the boundary model

Reads use `SemanticOperationGraph` → `SemanticPlan` → `ExecutionIR`. Mutations use `SemanticMutationOperationGraph` → mutation planning → mutation execution, with the same principle: semantic meaning is resolved and authorized before provider-specific work, and execution artifacts retain security provenance.

This is why GraphQL, MCP, JSON, AI tools and direct .NET or Java callers do not need separate authorization architectures. They converge before the security-sensitive planning boundary.

## Layer 1 — Intent

Intent is what the caller wants.

Supported entry surfaces include:

- typed and dynamic fluent .NET APIs;
- typed/fluent Java APIs;
- JSON intent adapters in both runtimes;
- GraphQL adapters (Hot Chocolate for .NET, GraphQL Java for Java);
- MCP adapters in both runtimes;
- Microsoft.Extensions.AI tool integration for .NET;
- a LangChain4j integration module in Java (source and tests present; dependency/runtime verification pending);
- semantic mutation builders in both runtimes.

Intent is untrusted input.

## Layer 2 — Semantics

`Foundgine.Core.Semantic` defines application meaning:

![PlantUML diagram: ARCHITECTURE, diagram 7](assets/architecture-plantuml-07.svg)

It also defines request graphs, filters, ordering, pagination, logical traversals, mutation semantics, capability descriptions, and security context contracts.

The semantic model is not the database schema.

## Layer 3 — Metadata

`Foundgine.Core.Semantic.Metadata` describes structural facts:

```text
entities
fields
primary keys
columns
direct relationships
model mappings
connections
conversions
```

In .NET, metadata can be generated by `Foundgine.Providers.Aot.Generator` from attributes or compiled from the fluent configuration API (`FoundgineModel.Build()`). In Java, the fluent `FoundgineModelConfiguration<T>` API and the `@FoundgineModel` / `@FoundgineModelEntityMap` annotations are available; `FoundgineAotProcessor` generates `GeneratedFoundgineMetadata.build()`. Both runtimes produce a `MetadataRegistry`; the generator names and build integration differ. See [`MAPPING.md`](MAPPING.md) and the [AOT overview](wiki/aot.md).

The important distinction is:

```text
Metadata = what structurally exists
Semantics = what the application means/exposes
```

## Layer 4 — Authorization

Authorization is applied to resolved semantic meaning.

The policy can constrain:

- entities;
- fields;
- relationships;
- read/write operations;
- conditional resource predicates.

Authorization is not a GraphQL concern, SQL concern, or AI concern.

A transport can help construct intent but cannot grant authority.

## Layer 5 — Planning

`Foundgine.Core.Semantic.Planning` turns authorized semantic operations into a provider-independent logical plan.

A read plan contains topology such as:

![PlantUML diagram: ARCHITECTURE, diagram 8](assets/architecture-plantuml-08.svg)

and semantic clauses such as filtering, ordering and pagination.

The plan must not contain SQL.

## Layer 6 — Rewriting and optimization

The planner can apply conservative rewrites.

A rewrite must preserve:

```text
semantic meaning
+
authorization
+
required security invariants
```

Where aggregate semantics or provider capabilities matter, explicit proof/capability gates are used.

Provider cost estimates are advisory only.

## Layer 7 — Execution

`Foundgine.Core.Execution` is the physical execution boundary.

It provides:

- `ExecutionIR`;
- provider compiler contracts;
- provider execution contracts;
- result materialization;
- execution evidence;
- provider security conformance;
- execution-time authorization revalidation;
- mutation execution coordination.

## Layer 8 — Providers

Execution providers include SQL and InMemory in both runtimes. Their implementation boundaries differ by language:

- .NET SQL uses ADO.NET; Java SQL uses JDBC.
- .NET and Java InMemory providers execute a deliberately limited subset over runtime-native rows.
- The ORM adapters are EF Core for .NET and JPA/Hibernate for Java. Their capability status is tracked separately in the [ORM provider matrix](wiki/orm-providers.md); source presence does not mean every capability is runtime-verified.

Two optional lexical-grounding candidate providers plug into the resolution layer without either depending on the other: Elasticsearch and PostgreSQL + pgvector. Both are present in the .NET and Java source trees. They return candidate references and evidence; they are not execution providers and do not authorize a candidate.

The existence of SQL and InMemory providers is an architectural test: the logical plan cannot depend on SQL-specific concepts.

## Migration tooling

Java's `foundgine-migrations` source module is schema tooling, not a runtime execution provider. It derives a PostgreSQL-oriented schema from `MetadataRegistry`, diffs it against a committed JSON snapshot, and writes versioned Flyway SQL, rollback SQL and migration history. Maven/runtime verification for this checkpoint remains pending. Its supported schema model covers tables, columns, keys, indexes, CHECK constraints and selected PostgreSQL extensions. It does not infer renames, partial/expression indexes, triggers, views, generated columns or safe data backfills; generated SQL must be reviewed. See the [migration module README](https://github.com/Foundgine/Foundgine/blob/main/src/java/foundgine-migrations/README.md) and the [Java starter migration example](https://github.com/Foundgine/Foundgine/blob/main/src/java/samples/foundgine-supply-chain/README.md).

### Retrieval strategies inside the SQL provider

Semantic resolution sometimes needs ranked candidates for an ambiguous reference — a name that doesn't exactly match, a fuzzy search term, or a "find things related to this" request. The .NET SQL provider and Java `PostgresRetrievalCandidateSource` expose PostgreSQL mechanisms selected per request behind the provider-neutral `RetrievalStrategy` contract:

![PlantUML diagram: ARCHITECTURE, diagram 9](assets/architecture-plantuml-09.svg)

`Fuzzy` and `FullText` use PostgreSQL's built-in `pg_trgm` and `tsvector`/`websearch_to_tsquery`. `Search` and `GraphSimilarity` are optional and require the `pg_search` and Apache AGE extensions respectively. `GraphSimilarity` runs a Cypher query through AGE over a semantic relationship (for example, finding suppliers similar to a given one by shared purchase-order neighbors) and returns ranked candidates. `Vector` is not implemented on the per-field approximate-candidate boundary; token-level vector retrieval instead lives in the separate PostgreSQL + pgvector provider, which implements the `ISemanticLexicalCandidateSource` boundary used by lexical grounding (see below and [`LEXICAL-GROUNDING.md`](LEXICAL-GROUNDING.md)).

Retrieval only ever produces candidates and evidence. It does not bypass semantic resolution, authorization, or planning — a candidate still has to resolve to a real semantic entity and pass authorization before it can appear in a plan.

## Transport adapters

Transport packages remain thin:

![PlantUML diagram: ARCHITECTURE, diagram 10](assets/architecture-plantuml-10.svg)

They do not become alternate planners.

## Security context

Authority is host-owned.

![PlantUML diagram: ARCHITECTURE, diagram 11](assets/architecture-plantuml-11.svg)

GraphQL variables, JSON properties, MCP arguments, and model-generated tool arguments must not be treated as authoritative identity/tenant/warrant material.

## Logical traversal

A semantic traversal can hide intermediate edges:

![PlantUML diagram: ARCHITECTURE, diagram 12](assets/architecture-plantuml-12.svg)

as:

```text
Customer.transactions
```

Resolution expands the path before authorization.

This prevents a shortcut from bypassing a denied intermediate entity or relationship.

## Mutations

Mutation semantics have their own algebra because writes require dependency, generated-value, approval, and security handling.

![PlantUML diagram: ARCHITECTURE, diagram 13](assets/architecture-plantuml-13.svg)

GraphQL mutation translation, MCP mutation tools, and direct mutation authoring all converge on this boundary.

## AOT

The AOT architecture moves stable metadata discovery into compilation:

![PlantUML diagram: ARCHITECTURE, diagram 14](assets/architecture-plantuml-14.svg)

The diagram illustrates the .NET generator path. The .NET source generator supports generated metadata and Native AOT-friendly handling. The Java annotation processor generates a `MetadataRegistry` at compile time; this does not by itself establish GraalVM Native Image compatibility. Neither approach makes arbitrary provider/application dependencies automatically AOT-compatible.

## Optional authority recovery

The .NET `Foundgine.Runtime.ControlPlane` recovery module is deliberately outside the core.

![PlantUML diagram: ARCHITECTURE, diagram 15](assets/architecture-plantuml-15.svg)

Java has a separate `com.foundgine.runtime.controlplane` package for approval/audit and authorization-key rotation. It is not the same full recovery feature set as the .NET module. Applications that do not need these optional control-plane functions do not need the packages.

## Dependency direction

The intended package structure is:

![PlantUML diagram: ARCHITECTURE, diagram 16](assets/architecture-plantuml-16.svg)

![PlantUML diagram: ARCHITECTURE, diagram 17](assets/architecture-plantuml-17.svg)

The exact project-reference graph contains additional supporting dependencies, but this is the architectural direction.

## What Foundgine is not

Foundgine is not intended to be:

- an ORM;
- a GraphQL server;
- an AI agent framework;
- an authorization server;
- a workflow engine;
- a database engine;
- a generic SQL builder.

Its purpose is the boundary between semantic intent and controlled execution.

---

Next: [Metadata → Semantics](METADATA-TO-SEMANTICS.md)

## Lexical grounding: retrieval proposes, semantics decides

Free-form language is resolved through a provider-neutral lexical candidate
boundary. Each token may be searched against every semantic kind (entity, node,
relationship, traversal, field, value, or operation). The highest retrieval
score is the first hypothesis, not truth.

![PlantUML diagram: ARCHITECTURE, diagram 18](assets/architecture-plantuml-18.svg)

The semantic graph is authoritative for topology. Approximate retrieval scores
never authorize a path and are never treated as probabilities. Database/provider
execution begins only after semantic resolution and authorization.
`Foundgine.Providers.Storage.Elasticsearch` and `Foundgine.Providers.Storage.PostgresVector` are two
interchangeable implementations of `ISemanticLexicalCandidateSource`; the
semantic layer depends on neither directly, and a deployment may use one,
the other, or both.

### A legal path is not necessarily the intended one

"Canonical semantic interpretation" above still only answers _is this
mapping legal_. It does not answer _is this mapping what the caller meant_,
and those can come apart: a single expression can be structurally valid
against two different fields, values, relationships, or root entities at
once, and retrieval score alone cannot break that tie in a principled way.

`SemanticLexicalResolver.Ground` inserts one more decision between
"canonical semantic interpretation" and authorization: it groups candidate
paths by what they actually mean (ignoring score and bridging route), and
only commits automatically when either one meaning dominates or the
remaining candidates all agree on that meaning. When two or more distinct
meanings remain within confidence range of each other, it returns
`GroundingOutcome.RequiresClarification` instead of authorizing whichever
one happened to score highest — see [Grounding decisions](GROUNDING-DECISIONS.md).
