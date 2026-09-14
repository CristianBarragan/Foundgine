# Architecture

Foundgine is a semantic execution layer between application intent and physical execution.

The central architectural rule is:

> **Callers describe intent. The semantic model defines meaning. Authorization determines authority. Planning defines logical execution. Providers define physical execution.**

## Canonical pipeline

<p align="center"><img src="assets/canonical-architecture.svg" alt="Foundgine canonical architecture showing the complete semantic lifecycle and parallel retrieval strategies." width="100%"></p>

The canonical lifecycle is: **Caller → Intent → Semantic Model → Semantic Operation Graph → Retrieval → Resolution → Authorization → Plan Binding → Execution IR → Provider → Execution → Evidence**. Other pages may focus on individual stages, but they must preserve this ordering.

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

Reads use `SemanticOperationGraph` → `SemanticPlan` → `ExecutionIR`. Mutations use `SemanticMutationOperationGraph` → mutation planning → execution src/csharp/security/conformance, with the same principle: semantic meaning is resolved and authorized before provider-specific work, and execution artifacts retain security provenance.

This is why GraphQL, MCP, JSON, AI tools and direct C# callers do not need separate authorization architectures. They converge before the security-sensitive planning boundary.


## Layer 1 — Intent

Intent is what the caller wants.

Supported entry surfaces include:

- typed fluent C#;
- dynamic fluent C#;
- JSON;
- GraphQL adapters;
- MCP;
- Microsoft.Extensions.AI tool integration;
- semantic mutation builders.

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

Metadata can be generated by `Foundgine.Providers.Aot.Generator`.

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

Current providers include:

```text
Foundgine.Providers.Storage.Sql
Foundgine.Providers.Storage.InMemory
```

Alongside the execution providers, two optional lexical-grounding candidate
providers plug into the resolution layer without either depending on the
other: `Foundgine.Providers.Storage.Elasticsearch` and `Foundgine.Providers.Storage.PostgresVector`.

SQL lowers the plan into parameterized SQL and executes through ADO.NET.

InMemory executes a deliberately limited subset over CLR-backed rows.

The existence of both providers is an architectural test: the logical plan cannot depend on SQL-specific concepts.

### Retrieval strategies inside the SQL provider

Semantic resolution sometimes needs ranked candidates for an ambiguous reference — a name that doesn't exactly match, a fuzzy search term, or a "find things related to this" request. `Foundgine.Providers.Storage.Sql` answers that through PostgreSQL mechanisms selected per request, all behind the same provider-neutral `RetrievalStrategy` contract:

![PlantUML diagram: ARCHITECTURE, diagram 9](assets/architecture-plantuml-09.svg)

`Fuzzy` and `FullText` use PostgreSQL's built-in `pg_trgm` and `tsvector`/`websearch_to_tsquery`. `Search` and `GraphSimilarity` are optional and require the `pg_search` and Apache AGE extensions respectively — `GraphSimilarity` runs a Cypher query through AGE over a semantic relationship (for example, finding suppliers similar to a given one by shared purchase-order neighbors) and returns ranked candidates, the same shape as any other strategy. `Vector` is not implemented on this per-field `IApproximateCandidateSource` boundary; token-level vector retrieval instead lives in `Foundgine.Providers.Storage.PostgresVector`, a `pgvector`-backed implementation of the separate `ISemanticLexicalCandidateSource` boundary used by lexical grounding (see below and [`LEXICAL-GROUNDING.md`](LEXICAL-GROUNDING.md)).

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

The AOT architecture moves stable topology into compilation:

![PlantUML diagram: ARCHITECTURE, diagram 14](assets/architecture-plantuml-14.svg)

This reduces runtime discovery work and supports Native AOT-friendly metadata handling.

It does not make arbitrary provider/application dependencies automatically Native-AOT compatible.

## Optional authority recovery

`Foundgine.Runtime.ControlPlane` is deliberately outside the core.

![PlantUML diagram: ARCHITECTURE, diagram 15](assets/architecture-plantuml-15.svg)

Applications that do not need a distributed authorization authority/recovery control plane do not need this package.

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

"Canonical semantic interpretation" above still only answers *is this
mapping legal*. It does not answer *is this mapping what the caller meant*,
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
