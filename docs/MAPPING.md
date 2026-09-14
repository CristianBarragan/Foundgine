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
