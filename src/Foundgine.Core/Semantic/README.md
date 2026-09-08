# Foundgine.Core.Semantic

`Foundgine.Core.Semantic` owns application meaning: the semantic model, structured intent, resolution, lexical grounding, validation, and semantic authorization.

## What is in this package

The package contains:

- semantic entities, fields, relationships, aliases, constraints, traversals, and operations;
- `SemanticModel`, the model-independent `SemanticEntityBuilder`, and the optional typed `SemanticEntityBuilder<TModel>`;
- `ReadIntent`, mutation intent and semantic result contracts;
- semantic identity and deterministic contract fingerprints;
- immutable `SemanticContractSnapshot` and contract providers;
- `SemanticOperationGraph` and graph safety/validation;
- semantic request resolution and lexical grounding;
- provider-neutral lexical candidate contracts;
- grounding budgets, ambiguity/clarification outcomes, and cancellation-aware resolution;
- authorization decisions, security capabilities and semantic security invariants;
- authorization evidence and binding types;
- security warrants, delegation, revocation, replay and trust-transition primitives.

## Boundary

The semantic layer answers **what the caller means** and **what the application exposes**. It does not compile SQL or execute a provider.

```text
Intent → semantic resolution → validation → authorization evidence
```

Retrieval sources such as Elasticsearch, pgvector, `pg_trgm`, or Apache AGE can supply candidates and evidence, but the frozen semantic contract remains authoritative.

## Install

```bash
dotnet add package Foundgine.Core.Semantic
```

Use this package directly when you need semantic modelling/resolution without the top-level `Foundgine` facade.

## Related packages

`Foundgine.Core.Semantic.Metadata` can discover structural facts into a semantic model. `Foundgine.Core.Semantic.Planning` consumes authorized semantic operations. `Foundgine.Core.Abstractions` contains the shared low-level contracts and identifiers.
