# Foundgine.Core.Semantic.Configuration

A fluent configuration API, similar in philosophy to EF Core's Fluent API but independent of it. It is a second way to produce the same `MetadataRegistry` that the `Foundgine*` attributes and the AOT generator produce; the two styles can be used side by side.

Each configuration class owns exactly one concern:

| Concern | Base class |
| --- | --- |
| Semantic entity, aliases, dimensions, relationships | `FoundgineModelConfiguration<TModel>` |
| Table, columns, key, indexes | `FoundginePersistenceConfiguration<TEntity>` |
| Model member to storage member | `FoundgineEntityMap<TModel, TEntity>` |
| Relationship to physical foreign key | `FoundgineConnectionConfiguration` |

`FoundgineModel` is the single registration point:

```csharp
public sealed class ErpModel : FoundgineModel
{
    public override void Configure(FoundgineModelBuilder model)
    {
        model.Configure<CustomerModelConfiguration>();
        model.Configure<OrderModelConfiguration>();
        model.Configure<ErpCustomerConfiguration>();
        model.Configure<ErpOrderConfiguration>();
        model.Configure<CustomerEntityMapping>();
        model.Configure<OrderEntityMapping>();      // every related model needs a map
        model.Configure<CustomerOrderConnection>();
    }
}

MetadataRegistry registry = new ErpModel().Build();
```

`Build()` validates the whole set and throws one `InvalidOperationException` listing every problem (missing key, unmapped relationship, ambiguous connection, mismatched types, ...). Configuration order does not matter.

To combine with attribute-generated metadata, call `new ErpModel().BuildInto(GeneratedMetadata.Registry)`. It throws if an entity, model or relationship id already exists, so neither source can silently overwrite the other.

## Behaviour worth knowing

- **Identity** is derived exactly as the generator does: entity and field ids from semantic names, column ids from `schema.table` + column name. Fluent and attribute metadata for the same names therefore get the same ids.
- **Storage name** is `schema.table` when a schema is given; the SQL providers already split on `.`.
- **Defaults**: the semantic entity name defaults to the model type name, the table to the storage type name, a column to the member name, a dimension to the member name.
- **Fields and columns** exist only for members that are mapped (or used by a key, index or connection). There is no reflection over properties; the API only reads member names out of the lambdas.
- **Relationships**: `HasRelationship(x => x.Customer).To<Customer>().WithMany(x => x.Orders)` also defines the inverse `Customer.Orders`, unless `Customer` declares that navigation itself. One connection configures both directions of the pair. When two models are related more than once, pass the navigation: `connections.HasRelationship<Order, Customer>(x => x.BillingCustomer)`.
- **Foreign key side**: `HasForeignKey` selects on the target storage type (Customer 1 - * Order). Use `HasForeignKeyOnSource` when the source owns the key.
- **Index** is recorded as an access-path hint on the leading column (`FieldMetadata.IsIndexed`). Uniqueness is not modelled.
- `HasMaxLength` and `IsRequired` are stored on `ColumnMetadata.MaxLength` and `ColumnMetadata.IsRequired` (new init-only properties).

## Not covered yet

- Source-generator discovery of `FoundgineModel` subclasses (compile-time registry, no runtime lambda reading). Today `Build()` runs at startup.
- `ConnectionMetadata` (model-to-entity connections), conversions and authorization predicates; those stay attribute-based.
- Composite keys and composite foreign keys.
- Type conversions between model and storage members (`To(...)` requires identical CLR types).
