# Foundgine.Extensions.GraphQL.HotChocolate.HotChocolate.Mutations

`Foundgine.Extensions.GraphQL.HotChocolate.HotChocolate.Mutations` is the translation layer for Hot Chocolate GraphQL mutations.

## What is in this package

- `HotChocolateMutationAdapter` — adapts GraphQL mutation operations into Foundgine mutation intent.
- `GraphQLMutationSemanticConverter` — converts GraphQL mutation input into provider-independent semantic mutation operations.
- `GraphQLMutationResultShaping` — shapes Foundgine mutation results for GraphQL.

## Boundary

```text
GraphQL mutation
      ↓
HotChocolateMutationAdapter
      ↓
semantic mutation intent/operation
      ↓
Foundgine.Core.Semantic.Planning / Execution
```

This package does not execute SQL and does not replace the Foundgine mutation authorization/security boundary.

For secure GraphQL mutation execution, see `FoundgineHotChocolateMutationExecutor` in
`Foundgine.Extensions` (`src/csharp/Foundgine.Extensions/GraphQL/HotChocolate/Execution/`), which
depends on this package for translation and adds the secure execution boundary.

## Install

```bash
dotnet add package Foundgine.Extensions.GraphQL.HotChocolate.HotChocolate.Mutations
```
