# Domain / persistence mapping (.NET, work in progress)

`Foundgine.Core.Mapping` keeps **domain types** and **persistence structures** (tables, columns) independent. Neither refers to the other; a mapping model is the only link. Tenant and ownership rules are declared once on the domain type and resolved to concrete table and column filters through the mapping.

!!! warning "Status: Checkpoint 2, work in progress"
    The metadata, builders, scope resolver and bridge were compiled and run by a console harness in earlier checkpoints. The **source emitter and the generated-vs-runtime checks were added later in a sandbox without a .NET SDK and have not been built or run**. Nothing here is wired into the EF Core provider or `Foundgine.Orm`, and there is no database execution. See `CHECKPOINT-2.md` and `MEASUREMENTS.md` at the repository root for the exact status and numbers. This page is separate from [Mapping and connections](MAPPING.md), which documents relationship connections.

## Concepts

| Type | Role |
|---|---|
| `MappingModel` | A set of `DomainMapping`s, built fluently with `Map<T>(...)`. |
| `DomainMapping` | One domain type: its tables, computed members, scope rules, relations and optional parent domain. |
| `TableBinding` | One table used by the type: role (`Root`, `Child`, `Join`, `Derived`), key column, bound columns, optional parent table / foreign key (child, join, table-per-type) and discriminator (table-per-hierarchy). |
| `ColumnBinding` | A member path (`Name`, `Address.Street`) bound to a column, with the recorded CLR type name. |
| `ScopeRule` | A `tenant` or `owner` rule: the member path and the claim it must equal. |
| `RelationBinding` | A relationship path, its target domain and, for many-to-many, the join table and its two columns. |

All of these are plain data (strings and lists), so a model built at runtime and a generated one can be compared field by field.

## Fluent API

```csharp
var model = new MappingModel()
    .Map<Order>(m =>
    {
        m.Tenant(x => x.TenantId);          // claim "tenant_id" by default
        m.Owner(x => x.CustomerId);         // claim "customer_id" by default
        m.Table("Orders", "Id").Convention();
        m.Child<OrderLine>(x => x.Lines, "OrderLines", "Id", "OrderId", t => t.Convention());
    });
```

| Member | What it declares |
|---|---|
| `Table(table, key, role)` | A table for this type. The key is the **physical column name**; for a root table it must be one of the bound columns or the build fails. |
| `.Convention(naming?)` | Binds every public scalar property to a column of the same name (or `naming(name)`). Nested value objects are not touched. |
| `.Column(x => x.Path, "col")` | Explicit binding: renames (`Name` to `FullName`) and flattened/owned values (`Address.Street` to `street`). |
| `.Parent(table, fk, parentKey)` | How the table is reached from its parent table (child, join or table-per-type derived). |
| `.Discriminator(col, value)` | Table-per-hierarchy discriminator. |
| `Computed(x => x.Member)` | A member with no column; excluded from column bindings. |
| `Tenant(...)`, `Owner(...)` | Scope rules declared once on the domain type. |
| `Child<TChild>(nav, table, key, fk, columns)` | An aggregate part in its own table, reached through a foreign key to the root table. |
| `ManyToMany<TTarget>(nav, joinTable, left, right)` | A join-table relationship; the join table is scoped through the left side. |
| `Base<TBase>()` | The type derives from (or is part of) another mapped type and inherits its scope rules. |

Only simple member paths are supported in the expressions.

## Scope resolution

`ScopeResolver.Resolve(model, "OrderLine")` returns the filters that must be applied when querying each table of that type, for example:

```text
OrderLines.OrderId->Orders.Id | OrderLines: Orders.TenantId = @tenant_id
```

reads: querying `OrderLines`, reach `Orders` through `OrderLines.OrderId = Orders.Id`, and require `Orders.TenantId` to equal the `tenant_id` claim.

- Child, join and table-per-type derived tables are scoped **through** their parent; no per-table rule is written.
- **Fail closed:** if a table cannot be scoped (a tenant column is not mapped and there is no parent to inherit from), resolution throws `MappingScopeException` instead of returning an unfiltered table.
- `ResolveLink(model, domain, path)` returns the filters for following a many-to-many link: the join table through the source side, plus the target entity's own rules.

## Using a mapping model

- `MappingBridge.Register(model, registry)` lowers the model into the existing `EntityMetadata` / `RelationshipMetadata` and registers it in a `MetadataRegistry`. For a 1:1 mapping the result matches what `FoundgineModel.Build()` produces from a separate domain class, persistence class and entity map. `MappingBridge.TableLayout` recovers which table a column lives in.
- `FoundgineModelBuilder.UseMappings(model)` makes `Build()` / `BuildInto()` produce one registry from the fluent compiler and the mappings. Startup validation reports every problem in one exception: a domain type mapped twice, mapped by both styles, or a table the tenant/owner rules cannot reach.

## Generated metadata: `MappingSourceEmitter`

For AOT and trimmed applications, a model built once with the fluent API can be emitted as plain C#:

```csharp
var source = MappingSourceEmitter.Emit(models, "MyApp.Generated", "AppMappings");
File.WriteAllText("AppMappings.g.cs", source);
```

The output is a static class with one method per model and an `All` dictionary:

```csharp
var model = AppMappings.All["Complex"]();   // a MappingModel built from constructor calls only
```

- It uses constructor calls and string literals only: no reflection, no expression trees, no lambdas, no JSON.
- Values are emitted exactly as recorded (CLR types stay the recorded strings), so generated and runtime metadata are equal field for field.
- Output is deterministic (ordinal ordering, no timestamps), so CI can regenerate and diff it.

### Verifying equivalence

The harness `samples/Foundgine.Mapping.Benchmark` has an `emit <path>` mode and checks that, for each of the 11 scenarios, the compiled generated model equals the runtime model (structure and resolved scope), that the committed generated file matches a fresh emit, and that the emitted source contains no reflection or JSON.

```bash
src/csharp/samples/Foundgine.Mapping.Benchmark/regen-generated.sh
```

The script builds, regenerates `Generated/ScenarioMappings.g.cs`, rebuilds with it compiled in and runs the checks. **Until you run it, the committed generated file is a stub and the new checks fail by design.**

### Relation to the Roslyn AOT generator

This emitter covers the **mapping model** only. The Roslyn `FoundgineMetadataGenerator` (see [AOT](AOT.md)) generates the attribute-based metadata and is a separate system; the two are not yet connected.

## What is not done

- Not wired into the EF Core provider or `Foundgine.Orm`.
- No execution against a database; correctness is shown at the metadata and scope-resolution level only.
- No comparison against Hot Chocolate + EF Core is verified: the EF comparator was written by the same author and never built. Do not quote its ratios externally (see `MEASUREMENTS.md`).
- The emitter and generated-vs-runtime checks are unbuilt and unrun (above).

---

Next: [AOT](AOT.md)
