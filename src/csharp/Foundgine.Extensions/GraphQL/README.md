# Foundgine.Extensions.GraphQL.HotChocolate

`Foundgine.Extensions.GraphQL.HotChocolate` is the query-side Hot Chocolate GraphQL adapter for Foundgine.

## What is in this package

- `GraphQLSchemaAdapter` — exposes/bridges Foundgine semantic schema information.
- `GraphQLSchemaDescriptor` — schema description support.
- `HotChocolateSemanticAdapter` — converts GraphQL selections into Foundgine semantic requests.
- `GraphQLVariableCoercer` — variable/value coercion at the GraphQL boundary.
- `GraphQLDirectiveEvaluator` — directive handling.
- `GraphQLResultShape` — GraphQL result-shape information.
- `GraphQLAdapterError` — adapter-level errors.

## Boundary

```text
GraphQL selection
      ↓
Foundgine.Extensions.GraphQL.HotChocolate
      ↓
provider-independent semantic intent
      ↓
Foundgine runtime
```

This package translates GraphQL. It does not itself implement the secure Foundgine execution boundary.

For secure query execution, add `Foundgine.Extensions.GraphQL.HotChocolate.HotChocolate.Execution`.

## Install

```bash
dotnet add package Foundgine.Extensions.GraphQL.HotChocolate
```
