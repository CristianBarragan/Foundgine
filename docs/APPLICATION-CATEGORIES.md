# What Foundgine is designed for

This page exists to answer four questions plainly, in order, before going
deeper into architecture: **what** Foundgine is, **how** it works at a
glance, **where** it fits in an application, and **what** it is designed
for — followed by the categories of applications the repository already
proves it against.

## What

Foundgine is a semantic execution boundary for .NET: a layer that sits
between "a caller expressed an intent" and "a provider executed something,"
and makes sure the second never happens without the first being resolved
against application-defined meaning and authorized against the caller's
actual permissions.

It is not a database, an ORM, a GraphQL server, an identity provider, a
workflow engine, or an autonomous agent framework. See
[Why Foundgine](WHY-FOUNDGINE.md) for the full contrast.

## How

At a glance, every caller — application code, GraphQL, JSON, MCP, an AI
agent — converges on the same pipeline:

![PlantUML diagram: APPLICATION-CATEGORIES, diagram 1](assets/application-categories-plantuml-01.svg)

The application defines what exists and what is allowed once. Every
caller-facing surface reuses that same decision instead of re-implementing
it. See [Architecture](ARCHITECTURE.md) for the full lifecycle.

## Where

Foundgine sits **below the transport, above the provider**:

![PlantUML diagram: APPLICATION-CATEGORIES, diagram 2](assets/application-categories-plantuml-02.svg)

Concretely, that means:

- it replaces the layer where a controller, resolver, or tool handler would
  otherwise hand-roll authorization checks, tenant filters, and query
  construction;
- it does not replace the provider underneath — `Foundgine.Providers.Storage.Sql` still talks
  to PostgreSQL, `Foundgine.Providers.Storage.InMemory` is a proof/test provider, and nothing
  stops an application from also using an ORM for ordinary CRUD persistence
  alongside it;
- it does not replace the transport in front — GraphQL, MCP, and JSON
  adapters remain thin translations into Foundgine's semantic intent, not
  separate execution architectures.

## What it's designed for

Foundgine is designed for applications where **more than one kind of
caller needs to reach the same data and operations, and the cost of one of
them getting authorization or meaning wrong is real.** The value is small
when there is exactly one caller and one hand-written path to the database.
It grows with:

- the number of distinct callers (endpoints, tools, transports) that would
  otherwise each reimplement authorization and query construction;
- how consequential a wrong or under-authorized operation would be
  (a report is cheap to get wrong; a funds transfer is not);
- how much of the caller's input is free-form language rather than a fixed,
  pre-validated shape.

It is explicitly **not** designed to let a model infer business policy,
choose an interpretation on the caller's behalf when the words are
genuinely ambiguous, or grant execution authority based on generated
intent. See [Grounding decisions](GROUNDING-DECISIONS.md) for how that
boundary is enforced even inside free-form language resolution.

## Categories of applications

The samples in this repository are not arbitrary demos — each one is the
proof case for a distinct category of application Foundgine targets.

### 1. Multi-transport enterprise backends

One hardened semantic and authorization core exposed through several
transports at once (GraphQL, MCP, JSON), so each transport adapter stays a
thin translation instead of its own security surface.

- `samples/Foundgine.SupplyChain` — the minimal MCP → application → semantic
  model → planning → SQL → PostgreSQL starter.
- `samples/Foundgine.SupplyChain.Advanced` — the full semantic proving ground
  for Metadata → Semantics → Authorization → Intent, including lexical
  grounding, ambiguity handling, retrieval strategies and adversarial tests.
- `benchmarks/AgentEndToEnd/Fixtures/SupplyChain.PenTest` — benchmark-only
  transport security regression coverage over GraphQL and MCP.

### 2. AI-agent tool execution boundaries

An agent (or any LLM-driven caller) proposes structured intent; Foundgine
remains the authority over what that intent means and whether it is
authorized, so a growing tool surface does not become a growing set of
independent, inconsistently-secured execution paths.

- `samples/Foundgine.SupplyChain` — the starter agent-facing MCP surface.
- `samples/Foundgine.SupplyChain.Advanced` — the same boundary at full
  application and semantic complexity.

See [AI agents](AI-AGENT.md) for the boundary this draws in detail.

### 3. High-assurance mutation workflows

Writes where the cost of a wrong authorization decision is high enough to
justify explicit dependency ordering, replay protection, deterministic
locking, and an execution receipt — deliberately *not* inferred from
natural language.

- `benchmarks/AgentEndToEnd/Fixtures/HighAssurance.Banking` — a `TransferFunds` mutation
  whose execution boundary revalidates tenant, ownership, account state,
  and daily limits, and produces an audit entry and receipt.
- `benchmarks/AgentEndToEnd/Fixtures/HighAssurance.Postgres` — the same capability against
  real PostgreSQL execution, transaction, and idempotency semantics.

### 4. Composite / cross-domain application models

Applications whose semantic model doesn't map 1:1 onto a single physical
schema — meaning is assembled from multiple underlying concepts, and the
semantic layer is what makes that assembly explicit instead of implicit in
query code.

- `samples/Foundgine.SupplyChain.Advanced` — its application-facing semantic
  model is explicitly separated from persistence/storage concepts.

### 5. Free-form / natural-language query surfaces

Callers that describe what they want in ordinary language rather than a
fixed request shape — a search box, a chat interface, an agent without a
rigid tool schema. This is the category [lexical grounding](LEXICAL-GROUNDING.md)
and [grounding decisions](GROUNDING-DECISIONS.md) exist for: turning free
text into a semantic interpretation without letting retrieval relevance
become authorization, and without letting a structurally valid path stand
in for a correctly understood one.

- `src/csharp/Foundgine.Providers/Foundgine.Providers.Storage.Elasticsearch`, `src/csharp/Foundgine.Providers/Foundgine.Providers.Storage.PostgresVector` — the two
  optional candidate-retrieval providers for this category.
- `samples/Foundgine.SupplyChain.Advanced/Semantic/Tests/Grounding` — a worked
  example of a materially ambiguous business term (`active supplier`)
  against a real generated semantic contract.

These categories are not mutually exclusive — the two SupplyChain samples alone
touch categories 1, 2, and 5 at once. They are meant as a map of *why* a
given piece of architecture exists, not a menu of separate products.

---

Previous: [Why Foundgine](WHY-FOUNDGINE.md) · Next: [Architecture](ARCHITECTURE.md)
