# Supply Chain Semantic Guide

This sample demonstrates the Foundgine architectural boundary using a deliberately
complex supply-chain domain.

## Pipeline

![PlantUML diagram: GUIDE, diagram 1](assets/guide-plantuml-01.svg)

### Metadata = what exists

`Domain/Domain.cs` is the CLR structural source observed by the AOT metadata producer.
The AOT generator emits the runtime registry. There is no parallel
`SupplyChainStructuralModels` graph and no semantic entity graph manually recreated
in `SupplyChainSemanticModel`.

### Semantics = what it means

`Semantics/SupplyChainSemanticModel.cs` starts with `SemanticModelBuilder.FromMetadata`, overlays the small typed `ManualSupplyChainSemanticModel` for `Product` and `ProductComponent`, and then adds the logical `Product.shipments` and `Product.supplierIncidents` traversals. The manual declarations are therefore part of the real runtime contract, while generated structural identities remain internal.

### Authorization = what may be exercised

`Authorization/SupplyChainAuthorization.cs` contains only Supply Chain policy data and
actor-specific values. Authorization still evaluates every expanded relationship and
intermediate entity in a traversal.

### Intent = what the caller wants

The tests use open semantic intent such as `Product → shipments` and complex mutation
intent. Intent is resolved against the discovered semantic graph before planning.

## Proving-ground tests

The test suite intentionally preserves the difficult cases from the earlier sample:

- recursive BOM traversal and cycle detection;
- tenant and warehouse isolation;
- sensitive-field authorization;
- relationship escalation attempts;
- mutation authorization and named operations;
- nested identity/value flow in mutations;
- adversarial invariant preservation;
- open-intent traversal expansion.

These tests are the acceptance criteria for the migration: the architecture is only
successful if the security behavior remains intact while the old generated semantic
model disappears.


## Boundary proof — metadata producer and semantic consumer

Semantic authoring remains open: callers may use the model-independent builder when an intent has no CLR type, or the typed builder when property-level CLR validation is useful. A semantic intent is never required to have a CLR model.

The sample deliberately keeps its structural declarations on the CLR domain types. The AOT generator observes `[FoundgineEntity]`, `[FoundgineField]`, and `[FoundgineRelationship]` declarations and emits `GeneratedMetadata.Registry`. `GeneratedMetadata.Build()` exposes that registry as `IMetadataCatalog`; the semantic layer consumes only that catalog.

This is the intended producer boundary: a future EF, database, or other metadata producer can replace the implementation without changing `SupplyChainSemanticModel`.
### Structural metadata contract

The AOT producer is a compile-time structural contract, not a passive serializer. Relationship declarations are rejected when the target entity, navigation target, foreign-key property, principal-key property, or key types are inconsistent. This keeps invalid topology out of `GeneratedMetadata.Registry` before semantic discovery or authorization can consume it.

