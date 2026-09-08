# Foundgine Supply Chain semantic boundary

This sample deliberately separates four concerns:

- **Metadata** — structural truth: entities, fields, keys, storage names and direct relationships.
- **Semantics** — application meaning: logical traversals such as `Product.shipments`.
- **Authorization** — who may exercise the discovered semantic surface.
- **Intent** — what the caller asks Foundgine to do.

Semantic authoring supports both model-independent declarations and optional strongly typed property selectors. The semantic intent surface is not constrained by a CLR model.

`SupplyChainSemanticModel` starts from `SemanticModelBuilder.FromMetadata(...)` and then composes `ManualSupplyChainSemanticModel` as a small typed semantic overlay. It does not recreate the structural schema or replace metadata identities.

Generated numeric identities remain internal metadata implementation details. Application semantic configuration uses logical names and resolves them against the discovered graph.

## Security proving-ground traversals

The sample exposes two application-level logical traversals:

- `Product.shipments` — `purchaseOrderLines -> purchaseOrder -> shipments`.
- `Product.supplierIncidents` — `purchaseOrderLines -> purchaseOrder -> supplier -> incidents`.

The second traversal is intentionally adversarial: capability discovery must suppress it when any intermediate entity or relationship is not readable. This demonstrates that logical traversal discovery is a security-aware projection of the complete semantic path, not a shortcut around authorization.

## The hand-authored illustration

`Semantics/ManualSupplyChainSemanticModel.cs` is **not** a second copy of the Supply Chain schema. It is a small typed semantic overlay and **is wired into the application pipeline** through `SupplyChainSemanticModel.Build()`.

It intentionally contains only two entities:

- `Product`
- `ProductComponent`

That small model demonstrates the typed `SemanticModelBuilder.Entity<TModel>(...)` API, property-based identities and fields, aliases, constraints, capabilities, and strongly typed relationships.

The running application uses the composed `SupplyChainSemanticModel`: it discovers the complete structural graph from AOT metadata, applies the two-entity manual semantic overlay, and then adds the application-level logical traversals. Every semantic consumer calls this composed model.

### Why keep the manual example small?

The purpose of this sample is to teach the metadata-first architecture. Manually reproducing every entity, field and relationship would create a second schema that can drift from the actual domain and would obscure the architectural boundary the sample is intended to demonstrate.

This is the intended composition pattern when an application has a metadata source but still needs curated semantic meaning: discover the complete structural graph first, then overlay only the small set of typed semantic declarations that cannot be inferred structurally.


## Weighted lexical evidence

The generated semantic contract also carries optional alias weights. In the
Supply Chain domain, entity, field, and relationship aliases are weighted
directly on the AOT declarations. `AliasWeightEvidenceGate` applies them only
when the current request is actually being lexically grounded, and only when a
weighted alias participates in that grounding path. Scope is preserved: a field
weight of `50` is evidence for that field only, never for its containing entity.
If a model was already known with certainty during processing, model-level
evidence is treated as `100`, but that certainty does not inflate field or
relationship evidence. With no lexical grounding, the feature is inert.

The corresponding coverage is in `Tests/Grounding/SupplyChainAliasWeightTests.cs`,
including AOT propagation, entity/field/relationship scopes, known-model `100`,
no-lexical-grounding behavior, boundary values, invalid weights, unweighted
aliases, threshold violations, and frozen-contract preservation.

The corresponding coverage is in
`Tests/Grounding/SupplyChainAliasWeightTests.cs`, including AOT propagation,
entity/field/relationship aliases, boundary values, invalid values,
unweighted aliases, threshold violations, and frozen-contract preservation.
