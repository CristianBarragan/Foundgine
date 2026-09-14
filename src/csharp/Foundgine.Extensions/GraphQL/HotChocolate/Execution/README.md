# Foundgine.Extensions — GraphQL/HotChocolate/Execution

The secure execution engine for the Foundgine Hot Chocolate GraphQL adapter.

## What is in this folder

- `FoundgineHotChocolateQueryExecutor` — executes GraphQL queries through the
  Foundgine semantic execution boundary.
- `FoundgineHotChocolateMutationExecutor` — executes GraphQL mutations through
  the Foundgine mutation authorization/execution boundary.
- Host-owned security execution-context integration for both.

## Why it lives here, in Extensions

`Foundgine.Extensions.GraphQL.HotChocolate` (schema adapters, query/mutation
translation, result shaping) performs GraphQL *translation*. This folder
performs secure *execution* on top of that translation:

```text
GraphQL
  ↓
Hot Chocolate adapter (Foundgine.Extensions.GraphQL.HotChocolate)
  ↓
FoundgineHotChocolateQueryExecutor / FoundgineHotChocolateMutationExecutor (here)
  ↓
Foundgine authorization / planning / execution (Foundgine.Runtime)
  ↓
provider (Foundgine.Providers)
```

These types previously lived under `Foundgine.Providers.GraphQL.HotChocolate`
on the reasoning that "execution" belongs alongside the other execution
folders in `Providers` (storage, MCP, model providers). In practice that made
`Foundgine.Providers` — a package that should only depend on `Foundgine.Core`
and `Foundgine.Runtime` — reach backward into `Foundgine.Extensions`, a
package documented as optional. Nothing else in `Foundgine.Providers` needed
that dependency; these two files were the entire reason for the edge. Since
both classes already depend on `HotChocolateSemanticAdapter` /
`HotChocolateMutationAdapter` (translation types that live in `Extensions`)
and only additionally need `Foundgine.Runtime` (which `Extensions` already
references), moving them here removes the inverted dependency entirely:
`Foundgine.Providers` no longer references `Foundgine.Extensions` at all.

A GraphQL transport must still not become the authority for identity,
tenant, or authorization state — that invariant is unchanged, it is just
enforced from `Extensions` now instead of from `Providers`.
