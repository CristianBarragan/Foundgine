# GraphQL HTTP endpoint (.NET)

`Foundgine.Extensions.GraphQL.Http` ships a ready-made HTTP front door for Foundgine GraphQL. It maps one bearer-authenticated `POST` route onto the secure query executor (and, when you opt in, the mutation executor), so a host no longer needs its own copy of the parsing, authorization and result-shaping code.

!!! note "Status"
    The endpoint was moved out of the Foundgine.Shop sample and the SupplyChain benchmark API in this snapshot. It has **not been compiled or run yet** (the authoring sandbox had no .NET SDK). Build the solution and run the benchmark/sample tests before relying on it. There is no Java equivalent; the Java GraphQL extension has executors but no HTTP endpoint.

## What it does

For every request the endpoint:

1. Returns **HTTP 401** when the caller is not authenticated. Authentication itself is the host's job (JWT bearer, Keycloak and so on).
2. Reads the JSON body (`query`, `variables`, `operationName`). Invalid JSON, a missing query or non-object variables return a GraphQL error (`BAD_REQUEST` / `BAD_USER_INPUT`), not an exception.
3. Parses the document and picks the operation. Introspection fields (`__...`) are ignored when finding root fields.
4. Runs your optional **`Authorize`** rule.
5. For queries, checks each root field against **`QueryRootRoles`**.
6. Executes through `FoundgineGraphQLQueryExecutor` (or `FoundgineGraphQLMutationExecutor` when mutations are allowed). Permissions, planning and provider execution stay inside the Foundgine boundary; the endpoint adds no authority of its own.
7. Rebuilds the executor's flat row/cell result into the nested GraphQL selection tree, keeping aliases and de-duplicating collection relationships by identity.

An authenticated caller who is not permitted never gets a 401; they get a GraphQL error with code `FORBIDDEN`.

## Minimal example

```csharp
using Foundgine.Extensions.GraphQL.Http;

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapFoundgineGraphQL(o =>
{
    o.QueryRootRoles["order"]     = ["manager", "customer"];
    o.QueryRootRoles["orderLine"] = ["manager"];   // customers reach lines only through their orders
});

app.Run();
```

The host must already register `FoundgineGraphQLQueryExecutor` and `SemanticModel` in DI. When `AllowMutations` is set it must also register `FoundgineGraphQLMutationExecutor`.

## Options

| Option | Default | Meaning |
|---|---|---|
| `Path` | `/graphql` | Route of the `POST` endpoint. Must not be empty. |
| `QueryRootRoles` | empty (invalid) | Query root field name to the roles allowed to **start** a query there. **Fail closed:** an unlisted root field is forbidden, and mapping with an empty dictionary throws at startup. |
| `AllowMutations` | `false` | Accept mutation operations. When false, a mutation gets `GRAPHQL_VALIDATION_FAILED` ("Only query operations are supported."). |
| `Authorize` | none | `Func<GraphQLHttpOperationContext, GraphQLAdapterError?>`. Return an error to refuse the request, or `null` to continue. |

The options object is copied when you call `MapFoundgineGraphQL`; changing it afterwards does not change an already-mapped endpoint.

### Why root-field roles exist

A role may hold a read capability on an entity only so related rows can be included, for example a customer reading order lines through their own order. If that customer could **start** a query at `orderLine`, they would read every tenant's lines, because only some entities carry a row-level predicate. `QueryRootRoles` states where each role may begin; everything else is reached through relationships and the row-level rules.

### `Authorize` and `GraphQLHttpOperationContext`

`Authorize` runs after parsing and **before** the root-role check, for queries and mutations alike. It receives:

| Member | Meaning |
|---|---|
| `User` | The authenticated `ClaimsPrincipal`. |
| `Operation` | `OperationType.Query` or `OperationType.Mutation`. |
| `RootFields` | Executable root fields of the selected operation (never empty). |
| `Variables` | The coerced variables. |

```csharp
o.Authorize = ctx =>
    ctx.User.IsInRole("customer") && !int.TryParse(ctx.User.FindFirst("customer_id")?.Value, out _)
        ? new GraphQLAdapterError("FORBIDDEN", "A customer_id claim is required.", Category: "FORBIDDEN")
        : null;
```

**Rule:** values used for authorization (tenant, customer id, roles) must come from the validated token, never from a GraphQL argument or variable. The one exception is a check that a caller-supplied value *equals* the token's value, as the benchmark does for `createSalesOrder`.

## Mutations

Set `o.AllowMutations = true` and register `FoundgineGraphQLMutationExecutor`. Mutation batches are executed as the adapter defines them. If a rule depends on the shape of a batch (the benchmark allows a customer exactly one `createSalesOrder` root), enforce it in `Authorize` using `RootFields` and `Variables`, since a batched second mutation would otherwise bypass per-field checks.

Root-field roles apply to **queries** only. Authorize mutations through `Authorize` and through Foundgine capabilities and warrants.

## Responses

| Situation | Result |
|---|---|
| Not authenticated | HTTP 401 |
| Success | `{ "data": { "<alias or root field>": [ ... ] } }` for queries; `{ "data": ... }` for mutations |
| Bad body, query or variables | `{ "errors": [{ "message", "extensions": { "code" } }] }` with `BAD_REQUEST` or `BAD_USER_INPUT` |
| Parse, validation or executor failure | The same error shape with the adapter's stable code |
| Not permitted | `FORBIDDEN` |

Query results contain **one** root field per response: the first executable root field. Document with several query roots are checked for permission on all of them but only the first is returned. This matches the previous behavior of both copies; do not rely on multi-root queries until that is extended.

## How the hosts use it

| Host | Rules |
|---|---|
| `samples/Foundgine.Comparison/Foundgine.Shop` | `ShopGraphQL.cs`: roots `customer`, `order`, `product` (manager, customer), `orderLine` (manager); a customer must carry `customer_id`. |
| `benchmarks/SupplyChain.Performance/Foundgine.SupplyChain.BenchmarkApi` | `BenchmarkGraphQL.cs`: four query roots, `AllowMutations`, customer ownership and the single `createSalesOrder` rule. |

## Moving an existing copy

1. Delete your local endpoint class (it probably declares its own `MapFoundgineGraphQL`).
2. Add `using Foundgine.Extensions.GraphQL.Http;`.
3. Move the role table into `QueryRootRoles` and claim checks into `Authorize`.
4. Call `app.MapFoundgineGraphQL(o => ...)`. The old parameterless call no longer exists, deliberately.

The library project references `Microsoft.AspNetCore.App`; it is bundled into the `Foundgine.Extensions` package.

## Limits

- Not compiled or run in this snapshot (see the note above).
- One root field is returned per query response (above).
- Only query and, optionally, mutation operations; subscriptions are rejected.
- It is not a GraphQL server: there is no schema validation or introspection beyond what the Foundgine adapters do.

---

Next: [Extensions](wiki/extensions.md)
