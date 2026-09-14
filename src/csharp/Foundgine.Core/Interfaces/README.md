# Foundgine.Core.Abstractions

`Foundgine.Core.Abstractions` is the provider-independent contract layer shared by the Foundgine architecture.

## What is in this package

The package contains stable identifiers and contracts that allow the other layers to communicate without depending on SQL, GraphQL, MCP, or a particular database.

### Stable identifiers

- `ModelId`
- `EntityId`
- `FieldId`
- `RelationshipId`
- `ConnectionId`
- `ColumnId`
- `AuthorizationId`
- `SemanticIdentity`

These identifiers are deliberately distinct so a relationship, field, model, or physical column cannot be confused at an API boundary.

### Authorization vocabulary

- `AuthorizationDecision`
- `AuthorizationAccess`
- `AuthorizationOperation`
- `AuthorizationOperationName`
- `AuthorizationPredicate`

Authorization predicates are provider-independent; physical providers decide how to lower them.

### Mutation contracts

- `MutationSchema`
- shared mutation operation/value contracts used by higher layers.

## What this package does not contain

It does not implement semantic resolution, planning, SQL execution, GraphQL, MCP, AI integration, or a database provider.

## Install

```bash
dotnet add package Foundgine.Core.Abstractions
```

Most applications receive this transitively through `Foundgine`, but it can be referenced directly when building against the lower-level contracts.
