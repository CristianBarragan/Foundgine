# Open Intent API

Foundgine's intent surface is deliberately open. Applications expose a semantic model and authorization policy; callers do not need a pre-generated method or interface for every possible query.

## Open intent enters the canonical lifecycle

Typed, dynamic, JSON, MCP, GraphQL and AI callers all converge on the same semantic lifecycle after producing intent:

![PlantUML diagram: OPEN-INTENT-API, diagram 1](assets/open-intent-api-plantuml-01.svg)

## Typed and dynamic authoring

Typed authoring gives developers compile-time help:

```csharp
var result = await foundgine
    .Query<Customer>()
    .Select(c => new { c.Id, c.Name })
    .Where(c => c.TenantId == tenantId)
    .ExecuteAsync();
```

Dynamic authoring is intended for agents, MCP, JSON adapters, and other callers that discover the model at runtime:

```csharp
var result = await foundgine
    .Query("Customer")
    .Select("Id", "Name")
    .ExecuteAsync();
```

Both produce the same `ReadIntent` and therefore use the same resolution, authorization, planning and provider execution pipeline.

## Logical traversals

A semantic model may expose a logical traversal without pretending that it is a physical relationship.

For example, a model can define:

```text
Customer
  -> CustomerRelationship
  -> Contract
  -> Transaction
```

and expose the open-intent traversal `transactions`:

```csharp
.Traversal(
    Customer,
    "transactions",
    CustomerRelationships,
    RelationshipContract,
    ContractTransactions)
```

A dynamic caller can then ask for:

```csharp
foundgine.Query("Customer")
    .Select("Id")
    .Include("transactions", transactions =>
        transactions.Select("Id", "Amount"));
```

Foundgine expands the logical traversal before authorization:

```text
Customer
  -> CustomerRelationship
      -> Contract
          -> Transaction
```

The caller does not need to know those intermediate entities. The security system does.

### Security rule

A logical traversal never bypasses intermediate semantics. Entity policies, relationship policies, field visibility, row predicates and execution invariants remain attached to the expanded nodes and edges.

If access to `Contract` is denied, `Customer.transactions` cannot tunnel through it to expose `Transaction`.

Logical traversals are also included in semantic model versioning, so changing a route invalidates stale plan approvals and cache identity.

## Dynamic filters and ordering

Dynamic intent can compose filters and relationship filters:

```csharp
foundgine.Query("Customer")
    .Select("Id", "Name")
    .Where("TenantId", SemanticFilterOperator.Eq, tenantId)
    .WhereRelated(
        "transactions",
        SemanticRelationshipQuantifier.Some,
        transactions => transactions.Where("Amount", SemanticFilterOperator.Eq, 100));
```

A logical traversal used as a relationship filter is expanded into its real relationship chain before semantic validation. Multi-hop `None`/`All` quantification is intentionally rejected until the semantic algebra has an explicit path-quantifier representation; Foundgine must never silently produce a logically different query.

## Open mutations

Mutations use the same principle but are deliberately more explicit because they can change state.

`SemanticMutationIntentBuilder` lets a developer compose an open mutation graph by semantic names while retaining generated-value dependencies:

```csharp
var graph = new SemanticMutationIntentBuilder(model)
    .Create("PurchaseOrder", "order")
        .Set("SupplierId", supplierId)
        .Return("Id")
    .Create("PurchaseOrderLine", "line")
        .SetFrom("PurchaseOrderId", "order", "Id")
        .Set("ProductId", productId)
        .Set("Quantity", 25m)
        .Return("Id", "PurchaseOrderId")
    .Create("Shipment", "shipment")
        .SetFrom("PurchaseOrderId", "order", "Id")
        .Set("Quantity", 25m)
        .Return("Id")
    .Build();
```

This produces the same `SemanticMutationOperationGraph` consumed by the existing mutation planner, authorization layer and execution security gate. The builder does not know about SQL columns, transactions, provider-generated correlation carriers or storage implementation.

For high-assurance mutations, callers should still use the normal mutation execution boundary. The builder is an authoring convenience; it is not an authorization bypass.

## Design rule

> Open intent describes what callers may ask for. The semantic model describes what exists and how it connects. Authorization determines what the caller may actually use. Planning determines how the authorized meaning can execute.

The application therefore does not need to predict every future query or mutation while retaining a single authoritative security and execution pipeline.

---

Next: [Lexical grounding](LEXICAL-GROUNDING.md)
