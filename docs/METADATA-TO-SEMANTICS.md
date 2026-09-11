# Metadata → Semantics

Foundgine separates four responsibilities:

> **Metadata describes what exists. Semantic configuration describes what it means. Authorization describes what may be exercised. Intent describes what the caller wants.**

## Structural discovery

`Foundgine.Core.Semantic.Metadata` is the structural source of truth. Providers or generated metadata describe entities, fields, keys, CLR types and direct relationships.

`Foundgine.Core.Semantic` consumes that metadata through `IMetadataCatalog`:

```csharp
var model = SemanticModel.Discover(metadata);
```

or, when application-specific enrichment is required:

```csharp
var model = SemanticModelBuilder
    .FromMetadata(metadata)
    .Traversal(customer, "transactions",
        customerRelationships,
        relationshipContract,
        contractTransactions)
    .Build();
```

Discovery does **not** grant authorization or invent business capabilities.

## Logical traversal

A configured traversal such as:

![PlantUML diagram: METADATA-TO-SEMANTICS, diagram 1](assets/metadata-to-semantics-plantuml-01.svg)

can expand to:

![PlantUML diagram: METADATA-TO-SEMANTICS, diagram 2](assets/metadata-to-semantics-plantuml-02.svg)

The expanded relationship path remains part of the semantic graph. Authorization and planning therefore see the real dependencies rather than an opaque shortcut.

## Developer rule

Do not configure facts that Foundgine can prove from structural metadata.

Configure meaning that structural metadata cannot infer:

- logical/business traversals;
- capability exposure;
- authorization policy;
- mutation requirements and invariants;
- application-specific semantic names.

## Supply Chain reference

The canonical Supply Chain sample intentionally keeps this enrichment in:

```text
src/csharp/samples/Foundgine.SupplyChain/Application/SupplyChainSemanticConfiguration.cs
```

It no longer has a separate `SupplyChain.Semantics` project. This is an architectural acceptance criterion: ordinary structural discovery must continue to work without an application-owned semantic framework.

The intentionally difficult `Foundgine.SupplyChain.Advanced` showcase remains a separate example for mixed/manual semantics, recursive relationships, authorization and complex mutation planning.

---

Next: [Mapping and connections](MAPPING.md)
