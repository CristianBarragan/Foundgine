# Foundgine.Core.Semantic.Metadata

`Foundgine.Core.Semantic.Metadata` is Foundgine's structural metadata and discovery layer.

## What is in this package

It models **what exists** in an application/provider structure:

- `IMetadataCatalog`
- `MetadataRegistry`
- `EntityMetadata`
- `FieldMetadata`
- `ColumnMetadata`
- `RelationshipMetadata`
- `ModelMetadata`
- `ConnectionMetadata`
- `ConnectionFieldMetadata`
- `ConversionMetadata`
- `AuthorizationMetadata`
- `StorageEntityId`
- `ColumnReference`
- `IMetadataProvider`
- `SemanticModelDiscovery`

The registry supports registration and lookup of structural metadata. `SemanticModelDiscovery` can turn that metadata into a semantic model that the application can further enrich.

## Boundary

Metadata is structural information, not authorization.

```text
Metadata → semantic model → authorization → planning → execution
```

The package does not execute queries and does not decide what a caller is allowed to do.

## Install

```bash
dotnet add package Foundgine.Core.Semantic.Metadata
```

Use it when semantic structure should be discovered from application/provider metadata rather than assembled entirely by hand.
