# Why Foundgine

Foundgine exists to provide a stable execution boundary between **application intent** and **physical execution**.

The problem is not that applications lack APIs. The problem is that every new intent source can otherwise become responsible for understanding the application's model, relationships, authorization rules, and provider-specific execution details.

Foundgine centralizes that responsibility.

## The problem

A complex application may have several ways to express an operation:

```text
Application code
GraphQL
JSON
AI-generated intent
```

Without a common semantic execution layer, each surface tends to grow its own rules for:

- what entities and fields exist;
- which relationships can be traversed;
- which filters are valid;
- what the caller is authorized to access; and
- how the request becomes database or service operations.

That produces duplicated semantics and inconsistent security boundaries.

## The Foundgine model

Foundgine establishes one pipeline:

![PlantUML diagram: WHY-FOUNDGINE, diagram 1](assets/why-foundgine-plantuml-01.svg)

The intent source describes **what is requested**.

The semantic model describes **what the application exposes**.

Authorization determines **what this caller may do**.

The execution plan describes **what Foundgine will execute**.

The provider determines **how that operation is physically executed**.

## The tool-surface problem

The case where duplicated semantics is most costly is a single caller with many capabilities — an AI agent with a set of tools is the clearest example. If each tool independently implements its own authorization, tenant filtering, and query construction:

![PlantUML diagram: WHY-FOUNDGINE, diagram 2](assets/why-foundgine-plantuml-02.svg)

then an agent with 50 tools has up to 50 independent execution and security surfaces, each only as correct as the person who wrote that one tool. Routing every capability through the same semantic and authorization boundary instead means there is one place where "is this request meaningful, and is this caller allowed to make it" gets answered, no matter which tool or transport the request came through.

## Why not put this in the transport?

GraphQL is good at describing an API contract. JSON is good at representing structured data. AI systems are good at generating structured requests.

None of those should become the authority for application semantics or execution security.

For example:

![PlantUML diagram: WHY-FOUNDGINE, diagram 3](assets/why-foundgine-plantuml-03.svg)

The same semantic and authorization pipeline can therefore be reused regardless of how intent entered the application.

## Why not use an ORM directly?

ORMs solve a different problem.

An ORM primarily maps objects and persistence models:

```text
Application objects ↔ database
```

Foundgine maps structured intent to executable operations:

![PlantUML diagram: WHY-FOUNDGINE, diagram 4](assets/why-foundgine-plantuml-04.svg)

Foundgine does not try to replace object persistence, change tracking, migrations, lazy loading, or identity maps.

For ordinary CRUD persistence, an ORM remains the right tool.

## Why AI makes the boundary more important

AI can generate intent, but generated intent should not become generated authority.

The desired boundary is:

![PlantUML diagram: WHY-FOUNDGINE, diagram 5](assets/why-foundgine-plantuml-05.svg)

The model can request an operation. Foundgine decides whether that operation is meaningful and authorized and controls how it reaches the provider.

This makes AI a consumer of the execution layer rather than a dependency of the execution layer.

## Why mutations raise the stakes

Reads are useful; writes are where a wrong authorization decision is expensive. `src/csharp/benchmarks/AgentEndToEnd/Fixtures/HighAssurance.Banking` demonstrates this with a `TransferFunds` mutation: the execution boundary revalidates tenant, ownership, account state, and daily limits, holds deterministic locks across both accounts, applies debit and credit together, and produces an audit entry and execution receipt. The sample deliberately stops short of claiming Foundgine can infer financial business policy from natural language — it proves the boundary holds under a consequential mutation, nothing more.

## What Foundgine currently proves

The active repository currently demonstrates:

- semantic modelling and resolution;
- authorization;
- provider-independent query and mutation planning;
- SQL execution against SQLite;
- nested traversal;
- deterministic plan fingerprints;
- execution evidence;
- AOT metadata generation;
- JSON intent; and
- Hot Chocolate GraphQL adapters.

It does not currently claim to be a general agent runtime, workflow engine, universal provider abstraction, or autonomous execution platform.

## The architectural rule

The most important rule is:

> **Foundgine Core must not depend on the transport used to express intent or the physical provider used to execute it.**

That rule is more important than any individual adapter or provider. It is what allows Foundgine to remain a semantic execution layer instead of becoming another GraphQL framework, ORM, or AI framework.

---

Next: [What Foundgine is designed for](APPLICATION-CATEGORIES.md)
