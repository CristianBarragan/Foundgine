# Roadmap

The roadmap is intentionally small. Foundgine's core boundaries are already established; future work should deepen those boundaries rather than turning the project into a collection of unrelated features.

## Current foundation

The following are already implemented and should be treated as the baseline:

- semantic model and open intent;
- semantic resolution/validation/normalization;
- granular semantic authorization;
- conditional authorization predicates;
- immutable semantic contract snapshots;
- provider-independent planning;
- execution IR;
- security-preserving plan rewrites;
- provider security conformance;
- SQL and InMemory providers;
- JSON, GraphQL, MCP and AI adapters;
- AOT metadata generation;
- mutation planning/execution boundaries;
- execution evidence;
- optional authority/recovery package outside the core.

## Near term

### 1. Simplify the public API

Reduce unnecessary ceremony in advanced APIs while preserving the architectural boundaries.

The goal is:

```text
simple common path
        +
explicit advanced path
```

rather than exposing every internal proof/IR type to ordinary application developers.

### 2. Strengthen provider composition

Make it easier to implement and compose providers without leaking provider details into semantics/planning.

A new provider should primarily need:

![PlantUML diagram: ROADMAP, diagram 1](assets/roadmap-plantuml-01.svg)

with explicit security conformance.

### 3. Improve real-world reference applications

Continue using the SupplyChain samples as architecture tests.

The reference applications should show:

- structural metadata;
- application semantic enrichment;
- authorization;
- typed/dynamic intent;
- MCP/GraphQL boundaries;
- mutations;
- PostgreSQL execution.

### 4. Measure before optimizing

Performance work should be driven by repeatable measurements from the actual provider.

The planner should not accumulate speculative physical optimizations.

### 5. Keep adapters thin

GraphQL, JSON, MCP, and AI should continue converging on the same semantic/execution contracts instead of developing transport-specific semantics.

## Medium term

Potential areas include:

- additional production providers;
- richer semantic expressions where demonstrated use cases require them;
- stronger semantic equivalence/property testing;
- improved operation canonicalization;
- more precise working-vs-output projection semantics;
- broader provider-aware planning evidence;
- stronger mutation ergonomics;
- a temporal candidate kind for lexical grounding (relative/absolute date
  expressions such as "last summer"), so an expression combining a
  temporal reference with an otherwise-resolvable entity/field no longer
  has to fail closed as a whole — see
  [Lexical grounding](LEXICAL-GROUNDING.md#adversarial-examples-where-this-gets-hard);
- named value-threshold declarations in the semantic model (e.g. an
  `AccountTier` field or a `HighValueAccount` flag) so domain concepts
  like "big accounts" can be represented in the contract instead of
  requiring the lexical layer to infer a business-specific cutoff, which
  it deliberately does not do today.

These should be introduced only when they solve a demonstrated application problem.

## Long term / optional

Possible extensions include:

- integrations with external identity/claims systems above the semantic policy boundary;
- more agent-framework adapters;
- additional durable authority/recovery integrations;
- richer event/action semantics.

These are optional extensions, not requirements for the core.

## Explicit non-goals

The roadmap does not aim to turn Foundgine into:

- an ORM;
- a GraphQL server;
- an LLM platform;
- an authorization server;
- a workflow engine;
- a general-purpose distributed database.

## Roadmap rule

Every new feature should answer three questions:

1. **Which architectural boundary owns it?**
2. **Can it be implemented without leaking provider/transport details upward?**
3. **What correctness/security test proves it?**

If those questions cannot be answered clearly, the feature should not be added yet.

---

Next: [History](history/README.md)
