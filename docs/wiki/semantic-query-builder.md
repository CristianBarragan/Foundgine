# Fluent semantic reads: `SemanticQuery` (C# and Java)

> **In plain words.** A semantic read says *which entity, which fields, which filters, which related rows*. Before, you
> built that by hand from low-level pieces (node ids, a field list for every child, an `And` wrapper, an optional options
> object). `SemanticQuery` is a small builder that does the bookkeeping for you. It produces exactly the same
> `SemanticOperation` as the hand-built version, so **authorization, planning and execution are unchanged**: the builder
> adds no permissions and removes none.

The builder exists in both runtimes with the **same name and the same members** (PascalCase in C#, camelCase in Java).
A shared vector file keeps them in step (see [Parity guarantee](#parity-guarantee)).

## Before and after

Read one order of one customer, with its lines. The hand-built form (what you wrote before):

```csharp
// C#
var lines = new SemanticReadNode(2, SalesOrderLine.Entity, SalesOrderLine.All,
    SalesOrder.Relationships.Lines, null, []);
var filter = new SemanticAndFilter([SalesOrder.Id.Eq(orderId), SalesOrder.CustomerId.Eq(customerId)]);
var op = new SemanticOperation(new SemanticReadNode(1, SalesOrder.Entity, SalesOrder.All,
    null, null, [lines], new SemanticQueryOptions(filter)));
```

```java
// Java (allFieldsOf: your own helper listing every field id of the entity)
var lines = new SemanticReadNode(2, lineEntity.entityId(), allFieldsOf(lineEntity),
        ordersToLines.id(), null, List.of(), null, null);
var filter = new SemanticAndFilter(List.of(orderIdEq, customerIdEq));
var op = new SemanticOperation(new SemanticReadNode(1, orderEntity.entityId(), allFieldsOf(orderEntity),
        null, null, List.of(lines), new SemanticQueryOptions(filter, List.of(), null, null, null), null));
```

With the builder:

```csharp
// C#
var op = SemanticQuery.From(metadata, SalesOrder.Entity)
    .Where(SalesOrder.Id.Eq(orderId), SalesOrder.CustomerId.Eq(customerId))
    .Include(SalesOrder.Relationships.Lines)
    .Build();
```

```java
// Java
var op = SemanticQuery.from(metadata, "SalesOrder")
        .whereEq("Id", orderId)
        .whereEq("CustomerId", customerId)
        .include(ordersToLines.id())
        .build();
```

What the builder fills in for you:

- **Fields.** All fields of the entity unless you call `Select`/`select`.
- **Children.** `Include(relationship)` looks the relationship up in the metadata, takes its **target entity** and selects all of
  that entity's fields. You never name the child entity.
- **Node ids.** Numbered `1, 2, 3...` in depth-first order.
- **Filters.** One condition is used as is; several are combined with **AND**. Query options are only created when something needs them.

## Member reference (identical in both languages)

| C# | Java | What it does |
|---|---|---|
| `SemanticQuery.From(metadata, entity)` | `SemanticQuery.from(metadata, entity)` | Start a query. `entity` is an `EntityId` or a name (exact match). |
| `Where(params conditions)` | `where(conditions...)` | Add filter expressions; repeated calls and several arguments are ANDed. |
| `WhereEq(field, value)` | `whereEq(field, value)` | Equals. `field` is a `FieldId` or a name. |
| `WhereNeq(field, value)` | `whereNeq(field, value)` | Not equals. |
| `WhereIn(field, params values)` | `whereIn(field, values...)` | Value is one of the list. |
| `OrderBy(terms)` / `OrderBy(name, direction)` | `orderBy(terms)` / `orderBy(name, direction)` | Ordering terms, in call order. |
| `Select(fields)` / `Select(names)` | `select(fields)` / `select(names)` | Choose fields by id or by name. |
| `Take(n)` / `Skip(n)` | `take(n)` / `skip(n)` | Limit and offset. |
| `Include(rel, configure?)` | `include(rel)` / `include(rel, configure)` | Load a related collection; the optional lambda refines the child query. |
| `Build()` | `build()` | Produce the `SemanticOperation`. |

**Names.** Field names are matched **case-insensitively** (`"id"` finds `Id`). Unknown names fail immediately with the same
message in both runtimes: `Unknown entity X` or `Unknown field Entity.Name`.

## Refining a child with a lambda

The `Include` lambda receives the child query, so everything above works on the related rows too:

```csharp
// C#
var op = SemanticQuery.From(metadata, "Warehouse")
    .Select("Id", "Name")
    .WhereEq("Region", "NZ")
    .Include(warehouseLots, lots => lots
        .Select("Sku", "OnHand")
        .WhereNeq("OnHand", 0)
        .OrderBy("Sku", SemanticSortDirection.Asc)
        .Take(50))
    .Build();
```

```java
// Java
var op = SemanticQuery.from(metadata, "Warehouse")
        .select("Id", "Name")
        .whereEq("Region", "NZ")
        .include(warehouseLots, lots -> lots
                .select("Sku", "OnHand")
                .whereNeq("OnHand", 0)
                .orderBy("Sku", SemanticSortDirection.ASC)
                .take(50))
        .build();
```

(C# takes the lambda as `Action<SemanticQuery>`, Java as `Consumer<SemanticQuery>`.)

## Running a query

The builder only builds the operation. Running it goes through the normal pipeline, so the caller's authorization is
applied exactly as before.

**C# (EF Core provider).** `FoundgineEfReader` runs authorization, planning and execution, and has two conveniences:

```csharp
// pass the query
var r = await reader.ReadAsync(reader.From(SalesOrder.Entity)
    .Where(SalesOrder.Id.Eq(orderId), SalesOrder.CustomerId.Eq(customerId))
    .Include(SalesOrder.Relationships.Lines), ct);

// or the lambda form: the reader supplies the starting query
var r = await reader.ReadAsync(SalesOrder.Entity, q => q
    .Where(SalesOrder.Id.Eq(orderId), SalesOrder.CustomerId.Eq(customerId))
    .Include(SalesOrder.Relationships.Lines), ct);

var order = r.FirstOrThrow("Sales order not found.");   // KeyNotFoundException if empty
```

**Java (JPA).** The sample semantics classes plan the read for a caller and return a provider plan; the lambda form is the
counterpart of the C# lambda read:

```java
ProviderPlan plan = semantics.planRead(caller, "Warehouse", q -> q
        .select(warehouseFields)
        .whereEq("Region", "NZ")
        .include(inventory.id(), lots -> lots.select(lotFields)));
```

The Java runtime has no reader class yet, so there is no `ReadAsync` counterpart; execution uses the provider plan with
the caller's execution context, as in the existing JPA samples.

## Generic CRUD next to the reader

The ORM providers also give you mechanical CRUD for every entity in the model, with no per-entity code. Reads still go
through the authorized path above; writes are **not** authorized by the repository, so check the capability first
(the sample application facades do).

```csharp
// C#: one-line registration, then the generic repository
services.AddFoundgineEf(new SupplyChainModel().Build(), (sp, o) => o.UseNpgsql(connectionString));

var repo = sp.GetRequiredService<IFoundgineRepository<Shipment>>();
await repo.GetAsync(id);
await repo.ListAsync(x => x.OrderId == 7);
await repo.AddAsync(shipment);
await repo.UpdateAsync(id, x => x.Status, "Delivered");
await repo.DeleteAsync(id);
repo.Query();     // IQueryable<T>: joins and projections stay open
repo.Context;     // the DbContext: raw EF is always reachable
```

```java
// Java: name-addressed gateway over the registry (counterpart of .NET FoundgineEntities).
// Reads need a ReadGate that returns an AUTHORIZED plan; writes are by single primary key.
var entities = new JpaEntities(entityManager, registry, readGate);
var warehouse = entities.entity("Warehouse");
warehouse.read(Map.of("Region", "NZ"), List.of("Id", "Name"));   // through the gate
warehouse.get(id);
warehouse.create(Map.of("Name", "Depot 3", "Region", "NZ"));      // no authorization here
warehouse.update(id, Map.of("Region", "AU"));
warehouse.delete(id);
```

The repository README (`foundgine-providers/.../storage/orm/README.md`) is the reference for `JpaEntities`.

## Parity guarantee

Both builders are held to **one shared vector file**, `src/conformance/semantic-query-vectors.json`
(spec `semantic-query-v1`), produced by an independent Python generator. Each case is a list of steps
(`select`, `whereEq`, `whereNeq`, `whereIn`, `orderBy`, `take`, `skip`, `include`) over a small Warehouse -> InventoryLot
model, and each runtime must build an operation whose canonical text equals the expected string, or throw an argument
error with the expected message. Example:

```text
#1 Warehouse(Id) where Region eq "NZ" [#2 InventoryLot(Sku, OnHand) via Lots where OnHand neq 0 order Sku asc limit 50]
```

Run them with `dotnet test --filter SemanticQueryConformance` and
`mvn -pl foundgine-core test -Dtest=SemanticQueryConformanceTest`. Never edit the JSON by hand; change
`src/conformance/tools/generate_semantic_query_vectors.py` and regenerate.

## Honest status

| Piece | .NET | Java |
|---|---|---|
| `SemanticQuery` builder | implemented-unverified | implemented-unverified |
| Shared conformance vectors and tests | written, **not yet run** | written, **not yet run** |
| Reader / plan helper (`ReadAsync(query)`, lambda form) | EF reader overloads; the Orm test suite ran 75 of 78 passing before a model-creation fix (not yet re-run) | `planRead(caller, entity, lambda)` on the samples; not compiled |
| Generic repository / gateway | `IFoundgineRepository<T>`, `AddFoundgineEf` | `JpaEntities`: source and unit test only, not compiled or run |

Not available yet on either side: predicate lambdas such as `o => o.Id == x && o.CustomerId == y`. Filters are built from
field handles and the `Eq/Neq/In` operators, as above.

## Related pages

- [ORM providers (EF Core and JPA)](orm-providers)
- [Semantic model and metadata](semantic-model)
- [Planning and execution](planning-execution)
- [Conformance and hardening](conformance-hardening)
