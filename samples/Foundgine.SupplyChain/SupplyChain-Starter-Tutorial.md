# Building the Foundgine Supply Chain "Starter" Sample — Step by Step

This walks you through building `samples/Foundgine.SupplyChain` from an empty
folder, piece by piece, so you understand *why* every file exists — not just
how to run it. By the end you'll have a single ASP.NET Core project that
exposes an MCP server backed by PostgreSQL, with every read and write routed
through Foundgine's semantic execution boundary instead of hand-written SQL.

> Reference implementation: `samples/Foundgine.SupplyChain` in this repo.
> Everything below matches that project exactly, so you can always cross-check
> against the real files if you get stuck.
>
> See also: `Foundgine-SupplyChain-Explained.md` in this same folder, which
> walks through *why* each concept below exists and what's required to set it
> up, section by section.

---

## 0. What you're building

```
AI agent / MCP client
        │
        ▼
      MCP tool call ("get_my_orders", "place_order", ...)
        │
        ▼
  Application layer (authorization + orchestration)
        │
        ▼
  Foundgine semantic model  →  Planner  →  SQL compiler
        │
        ▼
      PostgreSQL
```

The caller never writes SQL and never touches a connection string. It calls a
named capability with an `actor`, a `token`, and some arguments. Foundgine
turns that into a provider-neutral execution plan, and only *then* does SQL
get generated and run.

---

## 1. Prerequisites

- **.NET 9 SDK**
- **Docker Desktop** (for PostgreSQL)
- Git clone of this repository (you need the `Foundgine.Core`,
  `Foundgine.Runtime`, and `Foundgine.Providers` projects available, either as
  NuGet packages or as project references — this tutorial uses project
  references, exactly like the real sample)

Verify:

```bash
dotnet --version   # should print a 9.x version
docker --version
```

---

## 2. Create the project and add the 4 NuGet packages

Foundgine ships as **4 publishable NuGet packages**:

| Package | Role |
|---|---|
| `Foundgine.Core` | Contracts, semantic model, metadata, planning, serialization |
| `Foundgine.Runtime` | Orchestration, execution, control-plane, application-facing APIs |
| `Foundgine.Providers` | Storage (PostgreSQL), MCP, AI/model, and AOT provider implementations |
| `Foundgine.Extensions` | Optional caller-facing adapters (e.g. GraphQL/Hot Chocolate) |

For a normal application the **minimum footprint** is `Foundgine.Runtime` +
`Foundgine.Providers` — `Foundgine.Core` comes along transitively, and you
only add `Foundgine.Extensions` if you need GraphQL. The starter sample also
needs the MCP hosting package and the Postgres driver.

Create the project:

```bash
mkdir -p samples/MySupplyChain
cd samples/MySupplyChain
dotnet new web -n MySupplyChain
```

Add the packages (if you're consuming Foundgine from NuGet):

```bash
dotnet add package Foundgine.Runtime
dotnet add package Foundgine.Providers
dotnet add package Npgsql
dotnet add package ModelContextProtocol.AspNetCore
```

If you're building **inside this repository** (as the real sample does), use
project references instead so you're always building against the current
source, and add the AOT generator as an analyzer:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Npgsql" Version="10.0.3" />
    <PackageReference Include="ModelContextProtocol.AspNetCore" Version="2.2.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/csharp/Foundgine.Core/Foundgine.Core.csproj" />
    <ProjectReference Include="../../src/csharp/Foundgine.Runtime/Foundgine.Runtime.csproj" />
    <ProjectReference Include="../../src/csharp/Foundgine.Providers/Foundgine.Providers.csproj" />

    <!-- The Roslyn source generator that turns your [FoundgineModel]/[FoundgineEntity]
         attributes into a compiled metadata registry at build time. -->
    <ProjectReference Include="../../src/csharp/Foundgine.Providers/Foundgine.Providers.Aot.Generator/Foundgine.Providers.Aot.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false"
                      PrivateAssets="all"
                      SkipGetTargetFrameworkProperties="true"
                      UndefineProperties="TargetFramework" />
  </ItemGroup>
</Project>
```

> **Why a build-time generator?** Foundgine needs to know your entities,
> fields, and relationships *before* it can plan a query. Rather than
> reflecting over your types at runtime (slow, and AOT-hostile), the
> `Foundgine.Providers.Aot.Generator` analyzer reads your attributes at
> compile time and emits a `GeneratedMetadata`/`GeneratedSemanticModel` class
> you use directly in code — no runtime reflection, and it works under
> Native AOT.

Create the folder skeleton — these are architectural boundaries, not separate
assemblies, which is exactly why the starter is easy to read:

```
MySupplyChain/
  Domain/
  Application/
  Infrastructure/
    Mutations/
    Queries/
  Program.cs
```

---

## 3. Define the domain (application) model

This is the model your application logic talks about — customers, orders,
products — decorated with `[FoundgineModel]` so the AOT generator picks it
up. Create `Domain/Models.cs`:

```csharp
using Foundgine.Providers.Aot;

namespace Foundgine.SupplyChain.Domain.Models;

[FoundgineModel("Customer", Id = 101)]
public sealed class Customer
{
    public int Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";

    [FoundgineConnection(Id = 101, Name = "Orders")]
    public object Orders => throw new NotSupportedException();
}

[FoundgineModel("SalesOrder", Id = 102)]
public sealed class SalesOrder
{
    public int Id { get; init; }
    public int CustomerId { get; init; }
    public string Status { get; init; } = "";
    public decimal TotalAmount { get; init; }
}

[FoundgineModel("SalesOrderLine", Id = 103)]
public sealed class SalesOrderLine
{
    public int Id { get; init; }
    public int OrderId { get; init; }
    public int ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

[FoundgineModel("CatalogProduct", Id = 104)]
public sealed class CatalogProduct
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Sku { get; init; } = "";
    public decimal UnitPrice { get; init; }
}

[FoundgineModel("Supplier", Id = 105)]
public sealed class Supplier { public int Id { get; init; } public string Name { get; init; } = ""; public string Email { get; init; } = ""; }

[FoundgineModel("Category", Id = 106)]
public sealed class Category { public int Id { get; init; } public string Name { get; init; } = ""; }

[FoundgineModel("InventoryPosition", Id = 107)]
public sealed class InventoryPosition { public int Id { get; init; } public int WarehouseId { get; init; } public int ProductId { get; init; } public int QuantityOnHand { get; init; } public int ReorderLevel { get; init; } }

[FoundgineModel("Warehouse", Id = 108)]
public sealed class Warehouse { public int Id { get; init; } public string Name { get; init; } = ""; public string Location { get; init; } = ""; }

[FoundgineModel("Shipment", Id = 109)]
public sealed class Shipment { public int Id { get; init; } public int OrderId { get; init; } public int CarrierId { get; init; } public int WarehouseId { get; init; } public string TrackingNumber { get; init; } = ""; public string Status { get; init; } = ""; }

[FoundgineModel("Carrier", Id = 110)]
public sealed class Carrier { public int Id { get; init; } public string Name { get; init; } = ""; }
```

**What matters here:**
- `[FoundgineModel("Name", Id = N)]` registers the type as a semantic entity
  under a stable name and numeric id.
- `[FoundgineConnection]` marks a navigation you want to traverse
  semantically (`Customer.Orders`). The property body is never actually
  called — Foundgine intercepts it and resolves the relationship through the
  planner instead.
- Every id you assign here (101, 102, 103…) just needs to be **unique across
  your model** — pick your own numbering scheme.

---

## 4. Define the storage (ERP) entities

Your domain model describes *meaning*; your storage entities describe the
actual PostgreSQL tables and columns. Keeping them separate means a column
rename never leaks into your application code. Create
`Domain/StorageModels.cs`:

```csharp
using Foundgine.Providers.Aot;

namespace Foundgine.SupplyChain.Domain.Storage;

[FoundgineEntity("CustomerERP", StorageName = "customers", Id = 1)]
public sealed class CustomerERP
{
    [FoundgineField("Id", StorageName = "customer_id", Id = 1, IsPrimaryKey = true)]
    public int Id { get; init; }
    [FoundgineField("FirstName", StorageName = "first_name", Id = 2)]
    public string FirstName { get; init; } = "";
    [FoundgineField("LastName", StorageName = "last_name", Id = 3)]
    public string LastName { get; init; } = "";
    [FoundgineField("Email", StorageName = "email", Id = 4)]
    public string Email { get; init; } = "";

    [FoundgineRelationship(typeof(SalesOrderERP), "CustomerId", "Id", Id = 1, Name = "Orders")]
    public IReadOnlyList<SalesOrderERP> Orders { get; init; } = [];
}

[FoundgineEntity("SalesOrderERP", StorageName = "orders", Id = 2)]
public sealed class SalesOrderERP
{
    [FoundgineField("Id", StorageName = "order_id", Id = 1, IsPrimaryKey = true)] public int Id { get; init; }
    [FoundgineField("CustomerId", StorageName = "customer_id", Id = 2)] public int CustomerId { get; init; }
    [FoundgineField("Status", StorageName = "status", Id = 3)] public string Status { get; init; } = "";
    [FoundgineField("TotalAmount", StorageName = "total_amount", Id = 4)] public decimal TotalAmount { get; init; }
    [FoundgineRelationship(typeof(SalesOrderLineERP), "OrderId", "Id", Id = 2, Name = "Lines")] public IReadOnlyList<SalesOrderLineERP> Lines { get; init; } = [];
    [FoundgineRelationship(typeof(ShipmentERP), "OrderId", "Id", Id = 8, Name = "Shipments")] public IReadOnlyList<ShipmentERP> Shipments { get; init; } = [];
}

[FoundgineEntity("SalesOrderLineERP", StorageName = "order_items", Id = 3)]
public sealed class SalesOrderLineERP
{
    [FoundgineField("Id", StorageName = "order_item_id", Id = 1, IsPrimaryKey = true)] public int Id { get; init; }
    [FoundgineField("OrderId", StorageName = "order_id", Id = 2)] public int OrderId { get; init; }
    [FoundgineField("ProductId", StorageName = "product_id", Id = 3)] public int ProductId { get; init; }
    [FoundgineField("Quantity", StorageName = "quantity", Id = 4)] public int Quantity { get; init; }
    [FoundgineField("UnitPrice", StorageName = "unit_price", Id = 5)] public decimal UnitPrice { get; init; }
    [FoundgineRelationship(typeof(CatalogProductERP), "ProductId", "Id", Id = 3, Name = "Product")] public CatalogProductERP Product { get; init; } = null!;
}

[FoundgineEntity("CatalogProductERP", StorageName = "products", Id = 4)]
public sealed class CatalogProductERP
{
    [FoundgineField("Id", StorageName = "product_id", Id = 1, IsPrimaryKey = true)] public int Id { get; init; }
    [FoundgineField("Name", StorageName = "product_name", Id = 2)] public string Name { get; init; } = "";
    [FoundgineField("Sku", StorageName = "sku", Id = 3)] public string Sku { get; init; } = "";
    [FoundgineField("UnitPrice", StorageName = "unit_price", Id = 4)] public decimal UnitPrice { get; init; }
    [FoundgineRelationship(typeof(SupplierERP), "SupplierId", "Id", Id = 4, Name = "Supplier")] public SupplierERP Supplier { get; init; } = null!;
    [FoundgineRelationship(typeof(CategoryERP), "CategoryId", "Id", Id = 5, Name = "Category")] public CategoryERP Category { get; init; } = null!;
    [FoundgineRelationship(typeof(InventoryPositionERP), "ProductId", "Id", Id = 6, Name = "InventoryPositions")] public IReadOnlyList<InventoryPositionERP> InventoryPositions { get; init; } = [];
    [FoundgineField("SupplierId", StorageName = "supplier_id", Id = 5)] public int SupplierId { get; init; }
    [FoundgineField("CategoryId", StorageName = "category_id", Id = 6)] public int CategoryId { get; init; }
}

[FoundgineEntity("SupplierERP", StorageName = "suppliers", Id = 5)]
public sealed class SupplierERP
{
    [FoundgineField("Id", StorageName = "supplier_id", Id = 1, IsPrimaryKey = true)] public int Id { get; init; }
    [FoundgineField("Name", StorageName = "supplier_name", Id = 2)] public string Name { get; init; } = "";
    [FoundgineField("Email", StorageName = "email", Id = 3)] public string Email { get; init; } = "";
}

[FoundgineEntity("CategoryERP", StorageName = "categories", Id = 6)]
public sealed class CategoryERP
{
    [FoundgineField("Id", StorageName = "category_id", Id = 1, IsPrimaryKey = true)] public int Id { get; init; }
    [FoundgineField("Name", StorageName = "category_name", Id = 2)] public string Name { get; init; } = "";
}

[FoundgineEntity("InventoryPositionERP", StorageName = "inventory", Id = 7)]
public sealed class InventoryPositionERP
{
    [FoundgineField("Id", StorageName = "inventory_id", Id = 1, IsPrimaryKey = true)] public int Id { get; init; }
    [FoundgineField("WarehouseId", StorageName = "warehouse_id", Id = 2)] public int WarehouseId { get; init; }
    [FoundgineField("ProductId", StorageName = "product_id", Id = 3)] public int ProductId { get; init; }
    [FoundgineField("QuantityOnHand", StorageName = "quantity_on_hand", Id = 4)] public int QuantityOnHand { get; init; }
    [FoundgineField("ReorderLevel", StorageName = "reorder_level", Id = 5)] public int ReorderLevel { get; init; }
    [FoundgineRelationship(typeof(WarehouseERP), "WarehouseId", "Id", Id = 7, Name = "Warehouse")] public WarehouseERP Warehouse { get; init; } = null!;
}

[FoundgineEntity("WarehouseERP", StorageName = "warehouses", Id = 8)]
public sealed class WarehouseERP
{
    [FoundgineField("Id", StorageName = "warehouse_id", Id = 1, IsPrimaryKey = true)] public int Id { get; init; }
    [FoundgineField("Name", StorageName = "warehouse_name", Id = 2)] public string Name { get; init; } = "";
    [FoundgineField("Location", StorageName = "location", Id = 3)] public string Location { get; init; } = "";
}

[FoundgineEntity("ShipmentERP", StorageName = "shipments", Id = 9)]
public sealed class ShipmentERP
{
    [FoundgineField("Id", StorageName = "shipment_id", Id = 1, IsPrimaryKey = true)] public int Id { get; init; }
    [FoundgineField("OrderId", StorageName = "order_id", Id = 2)] public int OrderId { get; init; }
    [FoundgineField("CarrierId", StorageName = "carrier_id", Id = 3)] public int CarrierId { get; init; }
    [FoundgineField("WarehouseId", StorageName = "warehouse_id", Id = 4)] public int WarehouseId { get; init; }
    [FoundgineField("TrackingNumber", StorageName = "tracking_number", Id = 5)] public string TrackingNumber { get; init; } = "";
    [FoundgineField("Status", StorageName = "shipping_status", Id = 6)] public string Status { get; init; } = "";
    [FoundgineRelationship(typeof(SalesOrderERP), "OrderId", "Id", Id = 11, Name = "Order")] public SalesOrderERP Order { get; init; } = null!;
    [FoundgineRelationship(typeof(CarrierERP), "CarrierId", "Id", Id = 9, Name = "Carrier")] public CarrierERP Carrier { get; init; } = null!;
    [FoundgineRelationship(typeof(WarehouseERP), "WarehouseId", "Id", Id = 10, Name = "Warehouse")] public WarehouseERP Warehouse { get; init; } = null!;
}

[FoundgineEntity("CarrierERP", StorageName = "carriers", Id = 10)]
public sealed class CarrierERP
{
    [FoundgineField("Id", StorageName = "carrier_id", Id = 1, IsPrimaryKey = true)] public int Id { get; init; }
    [FoundgineField("Name", StorageName = "carrier_name", Id = 2)] public string Name { get; init; } = "";
}
```

**What matters here:**
- `StorageName` on the entity is the **table name**; on each field it's the
  **column name**. This is the only place SQL naming exists.
- `[FoundgineRelationship(typeof(Target), "LocalKey", "TargetKey", ...)]`
  describes a foreign-key join Foundgine can traverse in a plan (e.g.
  `Order → Lines → Product → Supplier`) without you writing a `JOIN`.
- Every `Id` value must be unique within its own scope (entity ids unique
  across entities, field ids unique within an entity, relationship ids
  unique across the whole model).

---

## 5. Map the domain model to the storage entities

Foundgine needs to know which `[FoundgineModel]` maps to which
`[FoundgineEntity]`. Create `Domain/Mappings.cs`:

```csharp
using Foundgine.Providers.Aot;
using Foundgine.SupplyChain.Domain.Models;
using Foundgine.SupplyChain.Domain.Storage;

namespace Foundgine.SupplyChain.Domain;

[FoundgineModelEntityMap(typeof(Customer), typeof(CustomerERP))]
[FoundgineModelEntityMap(typeof(SalesOrder), typeof(SalesOrderERP))]
[FoundgineModelEntityMap(typeof(SalesOrderLine), typeof(SalesOrderLineERP))]
[FoundgineModelEntityMap(typeof(CatalogProduct), typeof(CatalogProductERP))]
[FoundgineModelEntityMap(typeof(Supplier), typeof(SupplierERP))]
[FoundgineModelEntityMap(typeof(Category), typeof(CategoryERP))]
[FoundgineModelEntityMap(typeof(InventoryPosition), typeof(InventoryPositionERP))]
[FoundgineModelEntityMap(typeof(Warehouse), typeof(WarehouseERP))]
[FoundgineModelEntityMap(typeof(Shipment), typeof(ShipmentERP))]
[FoundgineModelEntityMap(typeof(Carrier), typeof(CarrierERP))]
[FoundgineConnectionMap(typeof(Customer), nameof(Customer.Orders), typeof(SalesOrderERP))]
internal static class SupplyChainSchemaMappings { }
```

This tiny file is the only place either the `Models` or `Storage` namespace
depends on the other — deliberately, so you can change your storage schema
without touching your application model, and vice versa.

**Checkpoint — build now:**

```bash
dotnet build
```

If this succeeds, the AOT generator has produced a `GeneratedMetadata` /
`GeneratedSemanticModel` class in the background containing your entire
model as compiled metadata. You never write or see this file by hand.

---

## 6. Nothing to write here — the generated model is already the semantic model

Earlier versions of this tutorial had you create a hand-written
`Semantics/SupplyChainSemanticModel.cs` wrapper at this point, aliasing the
generated registry into named constants. That wrapper is no longer
necessary: because `Domain/Mappings.cs` already pairs every `[FoundgineModel]`
to its `[FoundgineEntity]`, the AOT generator emits everything application
code needs directly — driven off that mapping, with no extra hand-written
step required:

- `GeneratedMetadata.Registry` — a `MetadataRegistry`, which already
  implements `IMetadataProvider`, so it can be registered in DI as-is.
- `GeneratedSemanticModel.<Model>.Entity` — the `EntityId` for each mapped
  model.
- `GeneratedSemanticModel.<Model>.<Field>` — one strongly-typed field
  accessor per property the model and its mapped entity share.
- `GeneratedSemanticModel.<Model>.Relationships.<Name>` — one
  `RelationshipId` constant per `[FoundgineRelationship]` property declared
  on the mapped storage entity.

**Why the mapping is what makes this possible, and why it's still
required:** the generator registers every `[FoundgineEntity]` into
`GeneratedMetadata.Registry` regardless of mapping, but it only emits a
`GeneratedSemanticModel.<Model>` class — the one with `.Entity` and
`.Relationships` — for models that appear in a
`[FoundgineModelEntityMap]`. Skip the mapping for a model and you keep raw,
name-lookup-only metadata for its entity; you lose the compile-time-checked
accessors entirely. So `Domain/Mappings.cs` isn't just documentation of
intent — it's the input the generator reads to decide which
`GeneratedSemanticModel` classes to emit in the first place.

In short: once your model, storage entities, and mapping are in place (steps
3–5), the semantic model is already fully generated. Application code in the
next steps references `GeneratedSemanticModel` and `GeneratedMetadata`
directly — there's no additional file to author.

---

## 7. Application layer: contracts and authorization

The Application layer is where "what a capability is allowed to do" lives —
completely separate from "how the query executes." Create
`Application/Contracts.cs`:

```csharp
namespace Foundgine.SupplyChain.Application;

public sealed record OrderLine(int ProductId, int Quantity);

public interface ISupplyChainQueries
{
    Task<object> GetOrders(int customerId, CancellationToken ct);
    Task<object> GetOrder(int customerId, int orderId, CancellationToken ct);
    Task<object> GetShipment(int customerId, int shipmentId, CancellationToken ct);
    Task<object> ListProducts(CancellationToken ct);
    Task<object> ListCustomers(CancellationToken ct);
    Task<object> GetProduct(int productId, CancellationToken ct);
    Task<object> GetInventory(int productId, CancellationToken ct);
    Task<object> ListSuppliers(CancellationToken ct);
}

public interface ISupplyChainMutations
{
    Task<object> UpdateInventory(int warehouseId, int productId, int quantity, CancellationToken ct);
    Task<object> CreateShipment(int orderId, int carrierId, int warehouseId, string trackingNumber, CancellationToken ct);
    Task<object> UpdateShipment(int shipmentId, string status, CancellationToken ct);
    Task<object> PlaceOrder(string actor, int customerId, OrderLine[] lines, string key, CancellationToken ct);
    Task<object> CancelOrder(string actor, int customerId, int orderId, CancellationToken ct);
}
```

Now `Application/Authorization.cs`. This is the security boundary: every MCP
tool call carries an `actor` and a `token`, and nothing runs until the actor
authenticates *and* is explicitly allowed to run that specific capability
against that specific customer:

```csharp
namespace Foundgine.SupplyChain.Application;

public interface ICapabilityAuthorizer
{
    void Demand(string actor, string token, string capability, int? customerId = null);
    void Authenticate(string actor, string token);
}

public sealed class SupplyChainAuthorizer : ICapabilityAuthorizer
{
    // Demo credential store only. A real deployment uses a proper identity
    // provider (JWT issuer, OAuth, hashed API-key vault) — never an in-code table.
    private static readonly Dictionary<string, string> ActorTokens = new(StringComparer.Ordinal)
    {
        ["alice"] = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_ALICE") ?? "alice-demo-token",
        ["bob"]   = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_BOB")   ?? "bob-demo-token",
        ["carol"] = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_CAROL") ?? "carol-demo-token",
        ["dave"]  = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_DAVE")  ?? "dave-demo-token",
        ["admin"] = Environment.GetEnvironmentVariable("SUPPLYCHAIN_TOKEN_ADMIN") ?? "admin-demo-token",
    };

    // Fixed, server-side actor -> customer mapping so nobody can grant
    // themselves an arbitrary customerId.
    private static readonly Dictionary<string, int> ActorCustomerMap = new(StringComparer.Ordinal)
    {
        ["alice"] = 1,
        ["bob"]   = 2,
    };

    private static readonly HashSet<string> CustomerScopedCapabilities = new(StringComparer.Ordinal)
    {
        "get_my_orders", "get_order", "get_shipment", "place_order", "cancel_order"
    };

    public void Authenticate(string actor, string token)
    {
        if (string.IsNullOrEmpty(actor)
            || string.IsNullOrEmpty(token)
            || !ActorTokens.TryGetValue(actor, out var expectedToken)
            || !FixedTimeEquals(token, expectedToken))
        {
            // Same message whether the actor exists or not, so the error
            // can't be used to enumerate valid actor names.
            throw new UnauthorizedAccessException("Invalid actor credentials.");
        }
    }

    public void Demand(string actor, string token, string capability, int? customerId = null)
    {
        Authenticate(actor, token);

        var allowed = actor switch
        {
            "alice" => new[] { "get_my_orders", "get_order", "get_product", "get_shipment", "place_order", "cancel_order" },
            "bob"   => new[] { "get_my_orders", "get_order", "get_product", "get_shipment", "place_order", "cancel_order", "list_customers" },
            "carol" => new[] { "get_product", "get_inventory", "update_inventory", "create_shipment", "update_shipment" },
            "dave"  => new[] { "get_product", "get_inventory", "list_products", "list_suppliers", "update_inventory" },
            "admin" => new[] { "get_my_orders", "get_order", "get_product", "get_shipment", "place_order", "cancel_order", "list_customers", "get_inventory", "update_inventory", "create_shipment", "update_shipment", "list_products", "list_suppliers" },
            _ => Array.Empty<string>()
        };

        if (!allowed.Contains(capability, StringComparer.Ordinal))
            throw new UnauthorizedAccessException($"Actor '{actor}' is not authorized for capability '{capability}'.");

        // Ownership check applies to EVERY actor for EVERY customer-scoped
        // capability. Only 'admin' may act across customers.
        if (customerId is not null
            && CustomerScopedCapabilities.Contains(capability)
            && !actor.Equals("admin", StringComparison.Ordinal))
        {
            if (!ActorCustomerMap.TryGetValue(actor, out var ownCustomerId) || ownCustomerId != customerId)
                throw new UnauthorizedAccessException("Actor is not authorized for the requested customer.");
        }
    }

    // Constant-time comparison so token checks don't leak length/prefix
    // information via response timing.
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
```

**Why this order matters:** authorization runs *before* any query is built.
A denied request never reaches the planner, never reaches SQL, and never
touches PostgreSQL.

Now the orchestrator that ties authorization to the two repositories, in
`Application/SupplyChainApplication.cs`:

```csharp
namespace Foundgine.SupplyChain.Application;

public sealed class SupplyChainApplication
{
    private readonly ICapabilityAuthorizer _auth;
    private readonly ISupplyChainQueries _queries;
    private readonly ISupplyChainMutations _mutations;

    public SupplyChainApplication(ICapabilityAuthorizer auth, ISupplyChainQueries queries, ISupplyChainMutations mutations)
    {
        _auth = auth; _queries = queries; _mutations = mutations;
    }

    public object DescribeCapabilities(string actor, string token)
    {
        _auth.Authenticate(actor, token);
        return new
        {
            actor,
            capabilities = actor switch
            {
                "alice" => new[] { "get_my_orders", "get_order", "get_product", "get_shipment", "place_order", "cancel_order" },
                "bob"   => new[] { "get_my_orders", "get_order", "get_product", "get_shipment", "place_order", "cancel_order", "list_customers" },
                "carol" => new[] { "get_product", "get_inventory", "update_inventory", "create_shipment", "update_shipment" },
                "dave"  => new[] { "get_product", "get_inventory", "list_products", "list_suppliers", "update_inventory" },
                "admin" => new[] { "get_my_orders", "get_order", "get_product", "get_shipment", "place_order", "cancel_order", "list_customers", "get_inventory", "update_inventory", "create_shipment", "update_shipment", "list_products", "list_suppliers" },
                _ => Array.Empty<string>()
            }
        };
    }

    public Task<object> GetMyOrders(string actor, string token, int customerId, CancellationToken ct)
    { _auth.Demand(actor, token, "get_my_orders", customerId); return _queries.GetOrders(customerId, ct); }

    public Task<object> GetOrder(string actor, string token, int customerId, int orderId, CancellationToken ct)
    { _auth.Demand(actor, token, "get_order", customerId); return _queries.GetOrder(customerId, orderId, ct); }

    public Task<object> GetShipment(string actor, string token, int customerId, int shipmentId, CancellationToken ct)
    { _auth.Demand(actor, token, "get_shipment", customerId); return _queries.GetShipment(customerId, shipmentId, ct); }

    public Task<object> ListProducts(string actor, string token, CancellationToken ct)
    { _auth.Demand(actor, token, "list_products"); return _queries.ListProducts(ct); }

    public Task<object> ListCustomers(string actor, string token, CancellationToken ct)
    { _auth.Demand(actor, token, "list_customers"); return _queries.ListCustomers(ct); }

    public Task<object> GetProduct(string actor, string token, int id, CancellationToken ct)
    { _auth.Demand(actor, token, "get_product"); return _queries.GetProduct(id, ct); }

    public Task<object> GetInventory(string actor, string token, int id, CancellationToken ct)
    { _auth.Demand(actor, token, "get_inventory"); return _queries.GetInventory(id, ct); }

    public Task<object> ListSuppliers(string actor, string token, CancellationToken ct)
    { _auth.Demand(actor, token, "list_suppliers"); return _queries.ListSuppliers(ct); }

    public Task<object> UpdateInventory(string actor, string token, int w, int p, int q, CancellationToken ct)
    { _auth.Demand(actor, token, "update_inventory"); return _mutations.UpdateInventory(w, p, q, ct); }

    public Task<object> CreateShipment(string actor, string token, int o, int c, int w, string t, CancellationToken ct)
    { _auth.Demand(actor, token, "create_shipment"); return _mutations.CreateShipment(o, c, w, t, ct); }

    public Task<object> UpdateShipment(string actor, string token, int id, string s, CancellationToken ct)
    { _auth.Demand(actor, token, "update_shipment"); return _mutations.UpdateShipment(id, s, ct); }

    public Task<object> PlaceOrder(string actor, string token, int customerId, OrderLine[] lines, string key, CancellationToken ct)
    { _auth.Demand(actor, token, "place_order", customerId); return _mutations.PlaceOrder(actor, customerId, lines, key, ct); }

    public Task<object> CancelOrder(string actor, string token, int customerId, int orderId, CancellationToken ct)
    { _auth.Demand(actor, token, "cancel_order", customerId); return _mutations.CancelOrder(actor, customerId, orderId, ct); }
}
```

---

## 8. Infrastructure: turn semantic operations into SQL

This is the piece that actually talks to Foundgine's planner. Create
`Infrastructure/Queries/SemanticSqlQueryExecutor.cs` — a small, reusable
helper that plans a `SemanticOperation`, compiles it to SQL, executes it
against PostgreSQL, and returns a fingerprint of exactly what ran:

```csharp
using System.Security.Cryptography;
using System.Text;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.IR;
using Foundgine.Providers.Storage.Sql;
using Npgsql;
using Foundgine.Providers.Storage.Sql.Query;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.SupplyChain.Infrastructure.Queries;

public sealed class SemanticSqlQueryExecutor
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly Planner _planner;
    private readonly IMetadataProvider _metadata;

    public SemanticSqlQueryExecutor(NpgsqlDataSource dataSource, Planner planner, IMetadataProvider metadata)
    {
        _dataSource = dataSource; _planner = planner; _metadata = metadata;
    }

    public async Task<(IReadOnlyList<ExecutionRow> Rows, string Fingerprint)> ExecuteAsync(
        SemanticOperation operation, CancellationToken ct)
    {
        var semanticPlan = _planner.Plan(operation);
        var sqlPlan = new SqlCompiler(_metadata).Compile(semanticPlan);

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        var result = await new SqlExecutionProvider(connection).ExecuteAsync(sqlPlan, new ExecutionContext(), ct);

        return (result.Rows, Fingerprint(sqlPlan.CommandText, sqlPlan.EffectiveParameters));
    }

    private static string Fingerprint(string sql, IEnumerable<SqlParameterBinding> parameters) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            sql + "|" + string.Join(';', parameters.Select(x => $"{x.Name}:{x.Value}")))))
            .ToLowerInvariant()[..24];
}
```

Then `Infrastructure/Queries/SupplyChainQueryRepository.cs`, which builds the
actual `SemanticOperation` for each read capability using your generated
model. Here's the pattern for the first two reads — the rest of your read
methods follow the same shape:

```csharp
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.IR;
using Foundgine.Generated;
using Foundgine.SupplyChain.Application;

namespace Foundgine.SupplyChain.Infrastructure.Queries;

public sealed class SupplyChainQueryRepository : ISupplyChainQueries
{
    private readonly SemanticSqlQueryExecutor _sql;
    public SupplyChainQueryRepository(SemanticSqlQueryExecutor sql) => _sql = sql;

    public async Task<object> GetOrders(int customerId, CancellationToken ct)
    {
        var operation = Read(
            GeneratedSemanticModel.SalesOrder.Entity,
            GeneratedSemanticModel.SalesOrder.All,
            GeneratedSemanticModel.SalesOrder.CustomerId.Eq(customerId),
            [GeneratedSemanticModel.SalesOrder.Id.Asc()]);

        var result = await _sql.ExecuteAsync(operation, ct);
        return new { customerId, orders = result.Rows.Select(x => x.Values).ToArray(), plan = result.Fingerprint };
    }

    public async Task<object> GetOrder(int customerId, int orderId, CancellationToken ct)
    {
        var line = new SemanticReadNode(2, GeneratedSemanticModel.SalesOrderLine.Entity,
            GeneratedSemanticModel.SalesOrderLine.All, GeneratedSemanticModel.SalesOrder.Relationships.Lines, null, []);

        var filter = new SemanticAndFilter([
            GeneratedSemanticModel.SalesOrder.Id.Eq(orderId),
            GeneratedSemanticModel.SalesOrder.CustomerId.Eq(customerId)]);

        var operation = new SemanticOperation(new SemanticReadNode(1, GeneratedSemanticModel.SalesOrder.Entity,
            GeneratedSemanticModel.SalesOrder.All, null, null, [line], new SemanticQueryOptions(filter)));

        var result = await _sql.ExecuteAsync(operation, ct);
        var row = result.Rows.FirstOrDefault() ?? throw new KeyNotFoundException("Sales order not found.");
        return new { order = row.Values, lines = result.Rows.Select(x => x.Values).ToArray(), plan = result.Fingerprint };
    }

    // ... GetShipment, ListProducts, ListCustomers, GetProduct, GetInventory,
    // ListSuppliers follow the same "build a SemanticOperation, execute it,
    // shape the response" pattern. Traversals (e.g. Shipment -> Order ->
    // Customer) use SemanticRelationshipFilter with the
    // GeneratedSemanticModel.<Model>.Relationships.<Name> constants covered
    // in step 6.
}
```

**What matters here:**
- `GeneratedSemanticModel.SalesOrder.CustomerId.Eq(customerId)` is a
  strongly-typed filter generated straight from your `[FoundgineField]`
  attributes — you never write a raw column name or a WHERE clause.
- `SemanticReadNode` composes nested reads (an order plus its lines) into a
  single semantic operation, which the planner turns into one round trip.
- Every response includes `plan` — the fingerprint returned by
  `SemanticSqlQueryExecutor` — so you always have evidence of exactly which
  compiled query produced a result.

For **mutations**, create
`Infrastructure/Mutations/SupplyChainMutationRepository.cs` implementing
`ISupplyChainMutations`. Writes go through Foundgine's separate
mutation-planning path (`Foundgine.Core.Semantic.Planning.Mutation` /
`Foundgine.Providers.Storage.Sql.Mutation`) because writes need explicit
dependency ordering and stronger guarantees than reads — server-side price
calculation, inventory checks, and idempotency-key handling for
`PlaceOrder`, for example. Follow the same "build the semantic mutation,
hand it to the SQL mutation compiler, execute it" shape as the query
repository above; see the real file in the repo for the full
`PlaceOrder`/`CancelOrder` implementations once you're comfortable with the
read path, or jump straight to the **Advanced tutorial**, which walks
through the high-assurance mutation pattern in detail.

---

## 9. Wire everything together in `Program.cs`

```csharp
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.SupplyChain.Application;
using Foundgine.SupplyChain.Infrastructure.Mutations;
using Foundgine.SupplyChain.Infrastructure.Queries;
using Foundgine.Generated;
using ModelContextProtocol.Server;
using Foundgine.Providers.Tools.MCP;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var cs = builder.Configuration["SupplyChainConnectionString"]
         ?? Environment.GetEnvironmentVariable("SupplyChainConnectionString")
         ?? throw new InvalidOperationException("SupplyChainConnectionString is required.");

builder.Services.AddSingleton<ICapabilityAuthorizer, SupplyChainAuthorizer>();
builder.Services.AddScoped<SupplyChainApplication>();

builder.Services.AddSingleton(NpgsqlDataSource.Create(cs));
// GeneratedMetadata.Registry already implements IMetadataProvider — see step 6.
builder.Services.AddSingleton<IMetadataProvider>(GeneratedMetadata.Registry);
builder.Services.AddSingleton(GeneratedMetadata.Registry);
builder.Services.AddSingleton<Planner>();
builder.Services.AddSingleton<SemanticSqlQueryExecutor>();
builder.Services.AddScoped<ISupplyChainQueries, SupplyChainQueryRepository>();
builder.Services.AddScoped<ISupplyChainMutations, SupplyChainMutationRepository>();

builder.Services.AddFoundgineMcp();
builder.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true).WithTools<SupplyChainMcpTools>();

var app = builder.Build();
app.MapMcp("/mcp");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (NpgsqlDataSource ds, CancellationToken ct) =>
{
    await using var c = await ds.OpenConnectionAsync(ct);
    await using var cmd = new NpgsqlCommand("SELECT 1", c);
    await cmd.ExecuteScalarAsync(ct);
    return Results.Ok(new { status = "ready" });
});
app.Run();

// Every tool requires a 'token' proving the caller controls the identity
// named by 'actor' - see Application/Authorization.cs.
[McpServerToolType]
public sealed class SupplyChainMcpTools
{
    private readonly IServiceScopeFactory _scopes;
    public SupplyChainMcpTools(IServiceScopeFactory scopes) => _scopes = scopes;

    private async Task<object> With(Func<SupplyChainApplication, Task<object>> f)
    {
        using var s = _scopes.CreateScope();
        return await f(s.ServiceProvider.GetRequiredService<SupplyChainApplication>());
    }

    [McpServerTool(Name = "describe_capabilities")] public Task<object> Describe(string actor, string token) => With(a => Task.FromResult(a.DescribeCapabilities(actor, token)));
    [McpServerTool(Name = "get_my_orders")] public Task<object> GetMyOrders(string actor, string token, int customerId, CancellationToken ct = default) => With(a => a.GetMyOrders(actor, token, customerId, ct));
    [McpServerTool(Name = "get_order")] public Task<object> GetOrder(string actor, string token, int customerId, int orderId, CancellationToken ct = default) => With(a => a.GetOrder(actor, token, customerId, orderId, ct));
    [McpServerTool(Name = "get_shipment")] public Task<object> GetShipment(string actor, string token, int customerId, int shipmentId, CancellationToken ct = default) => With(a => a.GetShipment(actor, token, customerId, shipmentId, ct));
    [McpServerTool(Name = "list_products")] public Task<object> ListProducts(string actor, string token, CancellationToken ct = default) => With(a => a.ListProducts(actor, token, ct));
    [McpServerTool(Name = "list_customers")] public Task<object> ListCustomers(string actor, string token, CancellationToken ct = default) => With(a => a.ListCustomers(actor, token, ct));
    [McpServerTool(Name = "get_product")] public Task<object> GetProduct(string actor, string token, int productId, CancellationToken ct = default) => With(a => a.GetProduct(actor, token, productId, ct));
    [McpServerTool(Name = "get_inventory")] public Task<object> GetInventory(string actor, string token, int productId, CancellationToken ct = default) => With(a => a.GetInventory(actor, token, productId, ct));
    [McpServerTool(Name = "list_suppliers")] public Task<object> ListSuppliers(string actor, string token, CancellationToken ct = default) => With(a => a.ListSuppliers(actor, token, ct));
    [McpServerTool(Name = "update_inventory")] public Task<object> UpdateInventory(string actor, string token, int warehouseId, int productId, int quantity, CancellationToken ct = default) => With(a => a.UpdateInventory(actor, token, warehouseId, productId, quantity, ct));
    [McpServerTool(Name = "create_shipment")] public Task<object> CreateShipment(string actor, string token, int orderId, int carrierId, int warehouseId, string trackingNumber, CancellationToken ct = default) => With(a => a.CreateShipment(actor, token, orderId, carrierId, warehouseId, trackingNumber, ct));
    [McpServerTool(Name = "update_shipment")] public Task<object> UpdateShipment(string actor, string token, int shipmentId, string status, CancellationToken ct = default) => With(a => a.UpdateShipment(actor, token, shipmentId, status, ct));
    [McpServerTool(Name = "place_order")] public Task<object> PlaceOrder(string actor, string token, int customerId, OrderLine[] lines, string idempotencyKey, CancellationToken ct = default) => With(a => a.PlaceOrder(actor, token, customerId, lines, idempotencyKey, ct));
    [McpServerTool(Name = "cancel_order")] public Task<object> CancelOrder(string actor, string token, int customerId, int orderId, CancellationToken ct = default) => With(a => a.CancelOrder(actor, token, customerId, orderId, ct));
}
```

Add `appsettings.json`:

```json
{
  "SupplyChainConnectionString": "Host=localhost;Port=4429;Database=my_supply_chain;Username=benchmark;Password=benchmark",
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*"
}
```

---

## 10. Stand up PostgreSQL and seed it

Create `docker-compose.yml` next to your project:

```yaml
services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_DB: my_supply_chain
      POSTGRES_USER: benchmark
      POSTGRES_PASSWORD: benchmark
    ports: ["4429:5432"]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U benchmark -d my_supply_chain"]
      interval: 2s
      timeout: 5s
      retries: 30
```

Start it:

```bash
docker compose up -d postgres
```

The schema only needs to match the `StorageName`/column names you declared
in step 4 — nothing more. Save this as `seed.sql` and run it against the
database:

```sql
CREATE TABLE IF NOT EXISTS suppliers (
  supplier_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  supplier_name VARCHAR(100) NOT NULL,
  email VARCHAR(100) UNIQUE
);
CREATE TABLE IF NOT EXISTS categories (
  category_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  category_name VARCHAR(50) NOT NULL UNIQUE
);
CREATE TABLE IF NOT EXISTS products (
  product_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  product_name VARCHAR(100) NOT NULL,
  sku VARCHAR(50) UNIQUE NOT NULL,
  category_id INT REFERENCES categories(category_id),
  supplier_id INT REFERENCES suppliers(supplier_id),
  unit_price DECIMAL(10,2) NOT NULL
);
CREATE TABLE IF NOT EXISTS warehouses (
  warehouse_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  warehouse_name VARCHAR(100) NOT NULL,
  location VARCHAR(255) NOT NULL
);
CREATE TABLE IF NOT EXISTS inventory (
  inventory_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  warehouse_id INT REFERENCES warehouses(warehouse_id),
  product_id INT REFERENCES products(product_id),
  quantity_on_hand INT DEFAULT 0,
  reorder_level INT DEFAULT 10,
  UNIQUE(warehouse_id, product_id)
);
CREATE TABLE IF NOT EXISTS customers (
  customer_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  first_name VARCHAR(50) NOT NULL,
  last_name VARCHAR(50) NOT NULL,
  email VARCHAR(100) UNIQUE NOT NULL
);
CREATE TABLE IF NOT EXISTS orders (
  order_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  customer_id INT REFERENCES customers(customer_id),
  status VARCHAR(20) NOT NULL DEFAULT 'Pending',
  total_amount DECIMAL(12,2) NOT NULL
);
CREATE TABLE IF NOT EXISTS order_items (
  order_item_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  order_id INT REFERENCES orders(order_id) ON DELETE CASCADE,
  product_id INT REFERENCES products(product_id),
  quantity INT NOT NULL CHECK (quantity > 0),
  unit_price DECIMAL(10,2) NOT NULL
);
CREATE TABLE IF NOT EXISTS carriers (
  carrier_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  carrier_name VARCHAR(100) NOT NULL
);
CREATE TABLE IF NOT EXISTS shipments (
  shipment_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  order_id INT REFERENCES orders(order_id),
  carrier_id INT REFERENCES carriers(carrier_id),
  warehouse_id INT REFERENCES warehouses(warehouse_id),
  tracking_number VARCHAR(100) UNIQUE,
  shipping_status VARCHAR(30) NOT NULL DEFAULT 'In Transit'
);

INSERT INTO suppliers (supplier_name, email) VALUES ('Acme Industrial', 'acme@example.test');
INSERT INTO categories (category_name) VALUES ('Hardware');
INSERT INTO warehouses (warehouse_name, location) VALUES ('Main Warehouse', 'Auckland, NZ');
INSERT INTO products (product_name, sku, category_id, supplier_id, unit_price) VALUES ('Widget', 'SKU-1001', 1, 1, 19.99);
INSERT INTO inventory (warehouse_id, product_id, quantity_on_hand) VALUES (1, 1, 100);
INSERT INTO customers (first_name, last_name, email) VALUES ('Alice', 'Anderson', 'alice@example.test');
INSERT INTO carriers (carrier_name) VALUES ('NZ Freight');
```

```bash
docker exec -i $(docker compose ps -q postgres) \
  psql -U benchmark -d my_supply_chain < seed.sql
```

---

## 11. Run it and test with MCP

```bash
export SupplyChainConnectionString="Host=localhost;Port=4429;Database=my_supply_chain;Username=benchmark;Password=benchmark"
dotnet run
```

The app now exposes:
- MCP: `http://localhost:5000/mcp` (or whatever port `dotnet run` prints)
- Health: `/health` and `/health/ready`

Check readiness:

```bash
curl http://localhost:5000/health/ready
```

Then call an MCP tool from any MCP-compatible client, or point an agent at
`/mcp`. Every call needs an `actor` and matching `token` — for example
`alice` / `alice-demo-token` — and read/write requests for `customerId: 1`
will succeed for Alice, while a request for `customerId: 2` will be rejected
by `SupplyChainAuthorizer` before it ever reaches Foundgine's planner.

---

## 12. Where to go next

You now have the full **MCP → application → semantic model → Foundgine
planning/execution → PostgreSQL** path working end to end, with basic reads,
a `place_order` mutation, and a `cancel_order` mutation.

The starter intentionally stops here. For the full semantic/authorization/
retrieval/security proving ground — claim-based authorization, a
high-assurance `PlaceOrder` with inventory and idempotency guarantees,
ambiguity resolution ("who is our top supplier?"), fuzzy/full-text/graph
retrieval strategies, and adversarial security tests — continue with the
**Advanced tutorial** (`SupplyChain-Advanced-Tutorial.md`).
