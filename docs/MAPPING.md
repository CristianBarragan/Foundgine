# Mapping and Connections

Foundgine does not populate domain models or EF entities.

EF remains the authority for the relational entity model: keys, columns and
relationships. Foundgine records the semantic connections that an application
can visit and compiles the useful correspondence information ahead of time.

## Default convention

A connection automatically uses ordinary member-name and type convention:

```text
Product.Id -> Contract.Id
```

No mapping code is required when the source and target members have the same
name and compatible type.

## Plain LINQ for exceptions

When convention is not enough, the connection can be described with an
ordinary LINQ expression that projects **values**, not an entity or model:

```csharp
[FoundgineConnection(typeof(Contract), Name = "Contract")]
public static Expression<Func<Product, object>> ContractProjection =>
    product => new
    {
        product.Id,
        ContractType = ProductConversions.ToContractType(product.ProductType)
    };
```

The anonymous projection is only a compile-time description. Foundgine never
executes it and never constructs a `Contract`.

The target member name comes from the anonymous projection member:

```csharp
ContractType = ...
```

An unrenamed member keeps the source member name:

```csharp
product.Id
```

This gives us plain C# for the exceptional cases without creating a mapping
DSL.

## Conversions

Conversions remain ordinary application code:

```csharp
[FoundgineConversion(typeof(ProductType), typeof(ContractType))]
public static ContractType ToContractType(ProductType value) => value switch
{
    ProductType.CreditCard => ContractType.CreditCard,
    ProductType.Mortgage => ContractType.Mortgage,
    ProductType.PersonalLoan => ContractType.PersonalLoan,
    _ => throw new ArgumentOutOfRangeException(nameof(value))
};
```

The AOT generator records the conversion identity. It does not invoke the
method while generating metadata.

## Fluent configuration

Attributes (or Java annotations) are not the only way to describe the model.
The fluent configuration API produces the same `MetadataRegistry` from plain
classes, so domain and storage types carry no Foundgine dependency. It is
independent of EF Core and similar in spirit to EF's fluent API. Both styles can
be used side by side.

Each configuration class owns one concern:

| Concern                                              | .NET base class                                  | Java base class                                  |
| ---------------------------------------------------- | ------------------------------------------------ | ------------------------------------------------ |
| Semantic entity, aliases, dimensions, relationships  | `FoundgineModelConfiguration<TModel>`            | `FoundgineModelConfiguration<TModel>`            |
| Table, columns, key, indexes                         | `FoundginePersistenceConfiguration<TEntity>`     | `FoundginePersistenceConfiguration<TEntity>`     |
| Model member to storage member                       | `FoundgineEntityMap<TModel, TEntity>`            | `FoundgineEntityMap<TModel, TEntity>`            |
| Relationship to physical foreign key                 | `FoundgineConnectionConfiguration`               | `FoundgineConnectionConfiguration`               |
| Model member to storage entity, conversions, authorization | `FoundgineModelConnectionConfiguration<TModel>` | `FoundgineModelConnectionConfiguration<TModel>` |

`FoundgineModel` is the single registration point. Configuration order does not
matter.

```csharp
public sealed class ErpModel : FoundgineModel
{
    public override void Configure(FoundgineModelBuilder model)
    {
        model.Configure<CustomerModelConfiguration>();
        model.Configure<ErpCustomerConfiguration>();
        model.Configure<CustomerEntityMap>();
        model.Configure<CustomerOrderConnection>();
    }
}

MetadataRegistry registry = new ErpModel().Build();
```

```java
public final class ErpModel extends FoundgineModel {
    @Override
    public void configure(FoundgineModelRegistrar model) {
        model.configure(CustomerSemantics.class);
        model.configure(CustomerStorage.class);
        model.configure(CustomerMap.class);
        model.configure(CustomerOrderConnections.class);
    }
}

MetadataRegistry registry = new ErpModel().build();
```

`Build()` / `build()` validates the whole set and throws one exception listing
every problem (missing key, unmapped relationship, ambiguous connection,
mismatched types). To combine with attribute- or annotation-generated metadata,
use `BuildInto(registry)` / `buildInto(registry)`; it throws if an entity, model
or relationship id already exists, so neither source silently overwrites the
other.

### Relationships and foreign keys

```csharp
connections.HasRelationship<Customer, SalesOrder>()
    .Using<CustomerERP, SalesOrderERP>()
    .HasForeignKey(o => o.CustomerId)      // key on the target storage type (1 - *)
    .References(c => c.Id);
```

One connection configures both directions of a pair. Use `HasForeignKeyOnSource`
(`hasForeignKeyOnSource`) when the source owns the key (* - 1).

### Conversions and authorization on connections

```csharp
connections.HasConnection<CustomerERP>(x => x.Customer)
    .Maps(x => x.CustomerId, e => e.Id)
    .Maps(x => x.CustomerCode, e => e.CodeNumber, int.Parse)   // conversion
    .Authorize<UserContext>("CanVisit",
        (user, customer) => user.TenantId == customer.TenantId);
```

`Maps(source, target)` needs identical types; the three-argument form records a
`ConversionMetadata` for the type pair. Foundgine never calls the delegate. The
authorization lambda is read as an expression tree and supports member access on
the two parameters, constants, `==`, `!=`, `&&`, `||` and `!`; anything else is
reported by `Build()`. The Java equivalent uses a small operand builder:
`.authorize(Ctx.class, "SameTenant", a -> a.context(Ctx::tenantId).eq(a.resource(Supplier::tenantId)))`.

### Limits

- Composite primary keys and composite foreign keys are not supported.
- Conversions exist on model connections only; entity maps require identical types.
- Connection fields are not inferred by name; list each `Maps(...)`.
- `FoundgineModel` subclasses are not discovered by the source generator, so
  `Build()` runs at startup.

## Connection semantics

A connection is a visitable semantic edge:

```text
Product
  |
  +-- Contract
        |
        +-- Id           <- Product.Id
        +-- ContractType <- Product.ProductType
                             via ToContractType
```

The relationship used to communicate with the target remains storage/EF
metadata. Foundgine's connection metadata describes the semantic visit and the
field correspondences needed by the planner.

## Design rule

Keep this boundary strict:

```text
EF entities        -> storage truth
Domain models      -> application truth
Connections        -> semantic topology
LINQ projection    -> exceptional correspondence
AOT                -> compile-time resolution
Plans              -> requested traversal
Provider           -> execution
```

No object mapper belongs in the foundation.

## AOT connection traversal

Once a connection is resolved, it is preserved as a distinct edge in the
provider-independent semantic graph:

```text
Root
 |
 +-- relationship --> Entity
 |
 +-- connection  --> Entity
```

The planner does not rediscover the connection. It emits
`ExecutionOperation.TraverseConnection` and carries the `ConnectionId` forward
into the execution plan.

That distinction is intentional:

- a relationship is the relational/EF edge;
- a connection is the application's semantic communication edge;
- the execution plan records which kind of edge the request traversed.

The provider can therefore decide how to execute the traversal without the
semantic layer learning SQL, joins, or ORM mechanics.

## Authorization expressions

Authorization for a semantic connection can be declared as an ordinary LINQ expression:

```csharp
[FoundgineAuthorization(10, Name = "CanVisitContract")]
public static Expression<Func<UserContext, Contract, bool>> CanVisitContract =>
    (user, contract) => user.TenantId == contract.TenantId;
```

The expression is a compile-time declaration. Foundgine records its source and
strongly typed context/resource identities in `AuthorizationMetadata`; it does
not instantiate or populate either object. Provider-specific lowering of the
predicate belongs to the planning/provider layer.

---

Next: [Open Intent API](OPEN-INTENT-API.md)
