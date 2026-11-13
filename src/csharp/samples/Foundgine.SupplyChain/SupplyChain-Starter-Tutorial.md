# Building the Foundgine Supply Chain "Starter" Sample — Step by Step

This walks you through building `src/csharp/samples/Foundgine.SupplyChain` from an empty
folder, piece by piece, so you understand _why_ every file exists — not just
how to run it. By the end you'll have a single ASP.NET Core project that
exposes an MCP server backed by PostgreSQL, with every read and write routed
through Foundgine's semantic execution boundary instead of hand-written SQL.

> Reference implementation: `src/csharp/samples/Foundgine.SupplyChain` in this repo.
> Everything below matches that project exactly, so you can always cross-check
> against the real files if you get stuck.
>
> See also: `Foundgine-SupplyChain-Explained.md` in this same folder, which
> walks through _why_ each concept below exists and what's required to set it
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
turns that into a provider-neutral execution plan, and only _then_ does SQL
get generated and run.

---

## 1. Prerequisites

- **.NET 10 SDK** (the sample targets `net10.0`; the Foundgine libraries are built from this same repository)
- **Docker Desktop** (for PostgreSQL)
- Git clone of this repository (you need the `Foundgine.Core`,
  `Foundgine.Runtime`, and `Foundgine.Providers` projects available, either as
  NuGet packages or as project references — this tutorial uses project
  references, exactly like the real sample)

Verify:

```bash
dotnet --version   # should print a 10.x version
docker --version
```

---

## 2. Create the project and add the 4 NuGet packages

Foundgine ships as **4 publishable NuGet packages**:

| Package                | Role                                                                  |
| ---------------------- | --------------------------------------------------------------------- |
| `Foundgine.Core`       | Contracts, semantic model, metadata, planning, serialization          |
| `Foundgine.Runtime`    | Orchestration, execution, control-plane, application-facing APIs      |
| `Foundgine.Providers`  | Storage (PostgreSQL), MCP, AI/model, and AOT provider implementations |
| `Foundgine.Extensions` | Optional caller-facing adapters (e.g. GraphQL/Hot Chocolate)          |

For a normal application the **minimum footprint** is `Foundgine.Runtime` +
`Foundgine.Providers` — `Foundgine.Core` comes along transitively, and you
only add `Foundgine.Extensions` if you need GraphQL. The starter sample also
needs the MCP hosting package and the Postgres driver.

Create the project:

```bash
mkdir -p src/csharp/samples/MySupplyChain
cd src/csharp/samples/MySupplyChain
dotnet new web -n MySupplyChain
```

Add the packages (if you're consuming Foundgine from NuGet):

```bash
dotnet add package Foundgine.Runtime --version 3.3.4
dotnet add package Foundgine.Providers --version 3.3.4
dotnet add package Npgsql
dotnet add package ModelContextProtocol.AspNetCore
```

If you're building **inside this repository** (as the real sample does), use
project references instead so you're always building against the current
source. This is the real `Foundgine.SupplyChain.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Npgsql" Version="10.0.3" />
    <PackageReference Include="ModelContextProtocol.AspNetCore" Version="2.3.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../Foundgine.Core/Foundgine.Core.csproj" />
    <ProjectReference Include="../../Foundgine.Runtime/Foundgine.Runtime.csproj" />
    <ProjectReference Include="../../Foundgine.Providers/Foundgine.Providers.csproj" />

    <ProjectReference
      Include="../../Foundgine.Providers/Foundgine.Providers.Aot.Generator/Foundgine.Providers.Aot.Generator.csproj"
      OutputItemType="Analyzer"
      ReferenceOutputAssembly="false"
      PrivateAssets="all"
      SkipGetTargetFrameworkProperties="true"
      UndefineProperties="TargetFramework"
    />
  </ItemGroup>
</Project>
```

> **Where does the model come from?** Foundgine needs to know your entities,
> fields, and relationships _before_ it can plan a query. In this sample you
> describe them in code, with the **fluent configuration API**
> (`Foundgine.Core.Semantic.Configuration`), and compile them once at startup
> with `new SupplyChainModel().Build()`. There is no attribute scanning and no
> runtime reflection over your types — the API only reads member names out of
> the lambdas you write. The `Foundgine.Providers.Aot.Generator` analyzer
> reference stays in the csproj for the attribute-based route, but a fluent
> model declares nothing for it to read, so it emits nothing here. (You can
> still mix both styles: `BuildInto(registry)` adds fluent metadata to an
> attribute-generated registry and refuses to overwrite an id that already
> exists.)

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
products. With the fluent API these are **plain classes**: no attributes, no
reference to `Foundgine.Providers`. Create `Domain/Models.cs`:

```csharp
namespace Foundgine.SupplyChain.Domain.Models;

// Plain application models. Everything Foundgine needs to know about them (entity
// names, relationships, storage mapping) lives in the fluent configuration classes
// in this project, so these types carry no Foundgine attributes and no dependency
// on Foundgine.Providers. The navigation members exist only so configuration can
// name relationships with lambdas; Foundgine never instantiates or populates them.

public sealed class Customer
{
    public int Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";

    public IReadOnlyList<SalesOrder> Orders { get; init; } = [];
}

public sealed class SalesOrder
{
    public int Id { get; init; }
    public int CustomerId { get; init; }
    public string Status { get; init; } = "";
    public decimal TotalAmount { get; init; }

    public IReadOnlyList<SalesOrderLine> Lines { get; init; } = [];
    public IReadOnlyList<Shipment> Shipments { get; init; } = [];
}

public sealed class SalesOrderLine
{
    public int Id { get; init; }
    public int OrderId { get; init; }
    public int ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }

    public CatalogProduct Product { get; init; } = null!;
}

public sealed class CatalogProduct
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Sku { get; init; } = "";
    public decimal UnitPrice { get; init; }

    public Supplier Supplier { get; init; } = null!;
    public Category Category { get; init; } = null!;
    public IReadOnlyList<InventoryPosition> InventoryPositions { get; init; } = [];
}

public sealed class Supplier
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
}

public sealed class Category
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

public sealed class InventoryPosition
{
    public int Id { get; init; }
    public int WarehouseId { get; init; }
    public int ProductId { get; init; }
    public int QuantityOnHand { get; init; }
    public int ReorderLevel { get; init; }

    public Warehouse Warehouse { get; init; } = null!;
}

public sealed class Warehouse
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Location { get; init; } = "";
}

public sealed class Shipment
{
    public int Id { get; init; }
    public int OrderId { get; init; }
    public int CarrierId { get; init; }
    public int WarehouseId { get; init; }
    public string TrackingNumber { get; init; } = "";
    public string Status { get; init; } = "";

    public SalesOrder Order { get; init; } = null!;
    public Carrier Carrier { get; init; } = null!;
    public Warehouse Warehouse { get; init; } = null!;
}

public sealed class Carrier
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}
```

**What matters here:**

- Nothing in this file says "Foundgine". Entity names, ids and relationships
  are configured in the next steps, so you can model types you don't own the
  same way.
- The navigation members (`Customer.Orders`, `SalesOrderLine.Product`, ...)
  exist only so configuration can **name a relationship with a lambda**
  (`x => x.Orders`). Foundgine never instantiates or populates them, and the
  property bodies are never called.
- You no longer assign numeric ids by hand. Entity, field and relationship
  ids are **derived from names**: entity ids from the semantic entity name,
  field ids from the entity name plus the member name, relationship ids from
  the entity name plus the navigation name. The same names always produce the
  same ids.

---

## 4. Define the storage (ERP) entities

These are the types that mirror your tables. They are plain too — scalar
members only. Create `Domain/StorageModels.cs`:

```csharp
namespace Foundgine.SupplyChain.Domain.Storage;

// Plain persistence types. Table, column and key configuration lives in
// PersistenceConfigurations.cs; relationships are semantic and are declared on the
// models (see ModelConfigurations.cs) and tied to these foreign keys in
// Mappings.cs, so the storage types hold scalar members only.

public sealed class CustomerERP
{
    public int Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";
}

public sealed class SalesOrderERP
{
    public int Id { get; init; }
    public int CustomerId { get; init; }
    public string Status { get; init; } = "";
    public decimal TotalAmount { get; init; }
}

public sealed class SalesOrderLineERP
{
    public int Id { get; init; }
    public int OrderId { get; init; }
    public int ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

public sealed class CatalogProductERP
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Sku { get; init; } = "";
    public decimal UnitPrice { get; init; }
    public int SupplierId { get; init; }
    public int CategoryId { get; init; }
}

public sealed class SupplierERP
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
}

public sealed class CategoryERP
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

public sealed class InventoryPositionERP
{
    public int Id { get; init; }
    public int WarehouseId { get; init; }
    public int ProductId { get; init; }
    public int QuantityOnHand { get; init; }
    public int ReorderLevel { get; init; }
}

public sealed class WarehouseERP
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Location { get; init; } = "";
}

public sealed class ShipmentERP
{
    public int Id { get; init; }
    public int OrderId { get; init; }
    public int CarrierId { get; init; }
    public int WarehouseId { get; init; }
    public string TrackingNumber { get; init; } = "";
    public string Status { get; init; } = "";
}

public sealed class CarrierERP
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}
```

Table, column and key names are **not** here. They live in a persistence
configuration, one class per storage type. Create
`Domain/PersistenceConfigurations.cs`:

```csharp
using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Domain.Storage;

namespace Foundgine.SupplyChain.Domain;

// Persistence configuration: table, key and column names only.

internal sealed class CustomerERPConfiguration : FoundginePersistenceConfiguration<CustomerERP>
{
    public override void Configure(FoundginePersistenceBuilder<CustomerERP> model)
    {
        model.ToTable("customers").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("customer_id");
        model.Property(x => x.FirstName).HasColumnName("first_name");
        model.Property(x => x.LastName).HasColumnName("last_name");
        model.Property(x => x.Email).HasColumnName("email");
    }
}

internal sealed class SalesOrderERPConfiguration : FoundginePersistenceConfiguration<SalesOrderERP>
{
    public override void Configure(FoundginePersistenceBuilder<SalesOrderERP> model)
    {
        model.ToTable("orders").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("order_id");
        model.Property(x => x.CustomerId).HasColumnName("customer_id");
        model.Property(x => x.Status).HasColumnName("status");
        model.Property(x => x.TotalAmount).HasColumnName("total_amount");
    }
}

internal sealed class SalesOrderLineERPConfiguration : FoundginePersistenceConfiguration<SalesOrderLineERP>
{
    public override void Configure(FoundginePersistenceBuilder<SalesOrderLineERP> model)
    {
        model.ToTable("order_items").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("order_item_id");
        model.Property(x => x.OrderId).HasColumnName("order_id");
        model.Property(x => x.ProductId).HasColumnName("product_id");
        model.Property(x => x.Quantity).HasColumnName("quantity");
        model.Property(x => x.UnitPrice).HasColumnName("unit_price");
    }
}

internal sealed class CatalogProductERPConfiguration : FoundginePersistenceConfiguration<CatalogProductERP>
{
    public override void Configure(FoundginePersistenceBuilder<CatalogProductERP> model)
    {
        model.ToTable("products").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("product_id");
        model.Property(x => x.Name).HasColumnName("product_name");
        model.Property(x => x.Sku).HasColumnName("sku");
        model.Property(x => x.UnitPrice).HasColumnName("unit_price");
        model.Property(x => x.SupplierId).HasColumnName("supplier_id");
        model.Property(x => x.CategoryId).HasColumnName("category_id");
    }
}

internal sealed class SupplierERPConfiguration : FoundginePersistenceConfiguration<SupplierERP>
{
    public override void Configure(FoundginePersistenceBuilder<SupplierERP> model)
    {
        model.ToTable("suppliers").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("supplier_id");
        model.Property(x => x.Name).HasColumnName("supplier_name");
        model.Property(x => x.Email).HasColumnName("email");
    }
}

internal sealed class CategoryERPConfiguration : FoundginePersistenceConfiguration<CategoryERP>
{
    public override void Configure(FoundginePersistenceBuilder<CategoryERP> model)
    {
        model.ToTable("categories").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("category_id");
        model.Property(x => x.Name).HasColumnName("category_name");
    }
}

internal sealed class InventoryPositionERPConfiguration : FoundginePersistenceConfiguration<InventoryPositionERP>
{
    public override void Configure(FoundginePersistenceBuilder<InventoryPositionERP> model)
    {
        model.ToTable("inventory").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("inventory_id");
        model.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
        model.Property(x => x.ProductId).HasColumnName("product_id");
        model.Property(x => x.QuantityOnHand).HasColumnName("quantity_on_hand");
        model.Property(x => x.ReorderLevel).HasColumnName("reorder_level");
    }
}

internal sealed class WarehouseERPConfiguration : FoundginePersistenceConfiguration<WarehouseERP>
{
    public override void Configure(FoundginePersistenceBuilder<WarehouseERP> model)
    {
        model.ToTable("warehouses").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("warehouse_id");
        model.Property(x => x.Name).HasColumnName("warehouse_name");
        model.Property(x => x.Location).HasColumnName("location");
    }
}

internal sealed class ShipmentERPConfiguration : FoundginePersistenceConfiguration<ShipmentERP>
{
    public override void Configure(FoundginePersistenceBuilder<ShipmentERP> model)
    {
        model.ToTable("shipments").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("shipment_id");
        model.Property(x => x.OrderId).HasColumnName("order_id");
        model.Property(x => x.CarrierId).HasColumnName("carrier_id");
        model.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
        model.Property(x => x.TrackingNumber).HasColumnName("tracking_number");
        model.Property(x => x.Status).HasColumnName("shipping_status");
    }
}

internal sealed class CarrierERPConfiguration : FoundginePersistenceConfiguration<CarrierERP>
{
    public override void Configure(FoundginePersistenceBuilder<CarrierERP> model)
    {
        model.ToTable("carriers").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("carrier_id");
        model.Property(x => x.Name).HasColumnName("carrier_name");
    }
}
```

**What matters here:**

- `ToTable("customers")` is the **table name** (use `"schema.table"` to
  include a schema) and `HasColumnName("customer_id")` is the **column name**.
  This is the only place SQL naming exists. A column defaults to the member
  name when you don't call `HasColumnName`.
- `HasKey(x => x.Id)` declares the single primary-key column. Foundgine
  entities have one key column: composite keys are not supported, and `Build()`
  tells you so if you try.
- Relationships are not declared on the storage types. You tie them to
  physical foreign keys in step 5.

---

## 5. Map the domain model to the storage entities

Three small files connect the two sides. Splitting them keeps either side from
depending on the other's CLR type.

**Semantic configuration** names each semantic entity and its relationships.
Create `Domain/ModelConfigurations.cs`:

```csharp
using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Domain.Models;

namespace Foundgine.SupplyChain.Domain;

// Semantic configuration: entity name and relationships only. Tables and columns
// are in PersistenceConfigurations.cs. ToEntity keeps the semantic entity names the
// attribute-based declarations used ("CustomerERP", ...); drop the calls to name
// entities after the models instead.

internal sealed class CustomerModelConfiguration : FoundgineModelConfiguration<Customer>
{
    public override void Configure(FoundgineModelBuilder<Customer> model)
    {
        model.ToEntity("CustomerERP");
        model.HasRelationship(x => x.Orders).To<SalesOrder>();
    }
}

internal sealed class SalesOrderModelConfiguration : FoundgineModelConfiguration<SalesOrder>
{
    public override void Configure(FoundgineModelBuilder<SalesOrder> model)
    {
        model.ToEntity("SalesOrderERP");
        model.HasRelationship(x => x.Lines).To<SalesOrderLine>();
        model.HasRelationship(x => x.Shipments).To<Shipment>();
    }
}

internal sealed class SalesOrderLineModelConfiguration : FoundgineModelConfiguration<SalesOrderLine>
{
    public override void Configure(FoundgineModelBuilder<SalesOrderLine> model)
    {
        model.ToEntity("SalesOrderLineERP");
        model.HasRelationship(x => x.Product).To<CatalogProduct>();
    }
}

internal sealed class CatalogProductModelConfiguration : FoundgineModelConfiguration<CatalogProduct>
{
    public override void Configure(FoundgineModelBuilder<CatalogProduct> model)
    {
        model.ToEntity("CatalogProductERP");
        model.HasRelationship(x => x.Supplier).To<Supplier>();
        model.HasRelationship(x => x.Category).To<Category>();
        model.HasRelationship(x => x.InventoryPositions).To<InventoryPosition>();
    }
}

internal sealed class SupplierModelConfiguration : FoundgineModelConfiguration<Supplier>
{
    public override void Configure(FoundgineModelBuilder<Supplier> model) =>
        model.ToEntity("SupplierERP");
}

internal sealed class CategoryModelConfiguration : FoundgineModelConfiguration<Category>
{
    public override void Configure(FoundgineModelBuilder<Category> model) =>
        model.ToEntity("CategoryERP");
}

internal sealed class InventoryPositionModelConfiguration : FoundgineModelConfiguration<InventoryPosition>
{
    public override void Configure(FoundgineModelBuilder<InventoryPosition> model)
    {
        model.ToEntity("InventoryPositionERP");
        model.HasRelationship(x => x.Warehouse).To<Warehouse>();
    }
}

internal sealed class WarehouseModelConfiguration : FoundgineModelConfiguration<Warehouse>
{
    public override void Configure(FoundgineModelBuilder<Warehouse> model) =>
        model.ToEntity("WarehouseERP");
}

internal sealed class ShipmentModelConfiguration : FoundgineModelConfiguration<Shipment>
{
    public override void Configure(FoundgineModelBuilder<Shipment> model)
    {
        model.ToEntity("ShipmentERP");
        model.HasRelationship(x => x.Order).To<SalesOrder>();
        model.HasRelationship(x => x.Carrier).To<Carrier>();
        model.HasRelationship(x => x.Warehouse).To<Warehouse>();
    }
}

internal sealed class CarrierModelConfiguration : FoundgineModelConfiguration<Carrier>
{
    public override void Configure(FoundgineModelBuilder<Carrier> model) =>
        model.ToEntity("CarrierERP");
}
```

`ToEntity("CustomerERP")` sets the semantic entity name. If you leave it out,
the name defaults to the model type name (`Customer`). The sample keeps the
`...ERP` names so the ids stay the same as the attribute-based version of this
sample.

**Entity maps and connections** say which storage member holds each model
member, and which foreign key backs each relationship. Create
`Domain/Mappings.cs`:

```csharp
using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Domain.Models;
using Foundgine.SupplyChain.Domain.Storage;

namespace Foundgine.SupplyChain.Domain;

// Schema-bound mappings. Keeping them out of both the application models and the
// ERP storage types prevents either from depending on the other CLR type.

// ---- model member -> storage member ---------------------------------------------

internal sealed class CustomerEntityMap : FoundgineEntityMap<Customer, CustomerERP>
{
    public override void Configure(FoundgineEntityMapBuilder<Customer, CustomerERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.FirstName).To(e => e.FirstName)
            .Property(m => m.LastName).To(e => e.LastName)
            .Property(m => m.Email).To(e => e.Email);
}

internal sealed class SalesOrderEntityMap : FoundgineEntityMap<SalesOrder, SalesOrderERP>
{
    public override void Configure(FoundgineEntityMapBuilder<SalesOrder, SalesOrderERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.CustomerId).To(e => e.CustomerId)
            .Property(m => m.Status).To(e => e.Status)
            .Property(m => m.TotalAmount).To(e => e.TotalAmount);
}

internal sealed class SalesOrderLineEntityMap : FoundgineEntityMap<SalesOrderLine, SalesOrderLineERP>
{
    public override void Configure(FoundgineEntityMapBuilder<SalesOrderLine, SalesOrderLineERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.OrderId).To(e => e.OrderId)
            .Property(m => m.ProductId).To(e => e.ProductId)
            .Property(m => m.Quantity).To(e => e.Quantity)
            .Property(m => m.UnitPrice).To(e => e.UnitPrice);
}

internal sealed class CatalogProductEntityMap : FoundgineEntityMap<CatalogProduct, CatalogProductERP>
{
    public override void Configure(FoundgineEntityMapBuilder<CatalogProduct, CatalogProductERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.Name).To(e => e.Name)
            .Property(m => m.Sku).To(e => e.Sku)
            .Property(m => m.UnitPrice).To(e => e.UnitPrice);
}

internal sealed class SupplierEntityMap : FoundgineEntityMap<Supplier, SupplierERP>
{
    public override void Configure(FoundgineEntityMapBuilder<Supplier, SupplierERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.Name).To(e => e.Name)
            .Property(m => m.Email).To(e => e.Email);
}

internal sealed class CategoryEntityMap : FoundgineEntityMap<Category, CategoryERP>
{
    public override void Configure(FoundgineEntityMapBuilder<Category, CategoryERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.Name).To(e => e.Name);
}

internal sealed class InventoryPositionEntityMap : FoundgineEntityMap<InventoryPosition, InventoryPositionERP>
{
    public override void Configure(FoundgineEntityMapBuilder<InventoryPosition, InventoryPositionERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.WarehouseId).To(e => e.WarehouseId)
            .Property(m => m.ProductId).To(e => e.ProductId)
            .Property(m => m.QuantityOnHand).To(e => e.QuantityOnHand)
            .Property(m => m.ReorderLevel).To(e => e.ReorderLevel);
}

internal sealed class WarehouseEntityMap : FoundgineEntityMap<Warehouse, WarehouseERP>
{
    public override void Configure(FoundgineEntityMapBuilder<Warehouse, WarehouseERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.Name).To(e => e.Name)
            .Property(m => m.Location).To(e => e.Location);
}

internal sealed class ShipmentEntityMap : FoundgineEntityMap<Shipment, ShipmentERP>
{
    public override void Configure(FoundgineEntityMapBuilder<Shipment, ShipmentERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.OrderId).To(e => e.OrderId)
            .Property(m => m.CarrierId).To(e => e.CarrierId)
            .Property(m => m.WarehouseId).To(e => e.WarehouseId)
            .Property(m => m.TrackingNumber).To(e => e.TrackingNumber)
            .Property(m => m.Status).To(e => e.Status);
}

internal sealed class CarrierEntityMap : FoundgineEntityMap<Carrier, CarrierERP>
{
    public override void Configure(FoundgineEntityMapBuilder<Carrier, CarrierERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.Name).To(e => e.Name);
}

// ---- relationship -> physical foreign key -----------------------------------------
// One connection configures both directions of a pair, so SalesOrder.Shipments and
// Shipment.Order share a single connection. HasForeignKey selects the key on the
// target storage type (one-to-many); HasForeignKeyOnSource selects it on the source
// (many-to-one).

internal sealed class SupplyChainConnections : FoundgineConnectionConfiguration
{
    public override void Configure(FoundgineConnectionBuilder connections)
    {
        // Customer 1 - * SalesOrder
        connections.HasRelationship<Customer, SalesOrder>()
            .Using<CustomerERP, SalesOrderERP>()
            .HasForeignKey(o => o.CustomerId)
            .References(c => c.Id);

        // SalesOrder 1 - * SalesOrderLine
        connections.HasRelationship<SalesOrder, SalesOrderLine>()
            .Using<SalesOrderERP, SalesOrderLineERP>()
            .HasForeignKey(l => l.OrderId)
            .References(o => o.Id);

        // SalesOrder 1 - * Shipment (also defines Shipment.Order)
        connections.HasRelationship<SalesOrder, Shipment>()
            .Using<SalesOrderERP, ShipmentERP>()
            .HasForeignKey(s => s.OrderId)
            .References(o => o.Id);

        // SalesOrderLine * - 1 CatalogProduct
        connections.HasRelationship<SalesOrderLine, CatalogProduct>()
            .Using<SalesOrderLineERP, CatalogProductERP>()
            .HasForeignKeyOnSource(l => l.ProductId)
            .References(p => p.Id);

        // CatalogProduct * - 1 Supplier
        connections.HasRelationship<CatalogProduct, Supplier>()
            .Using<CatalogProductERP, SupplierERP>()
            .HasForeignKeyOnSource(p => p.SupplierId)
            .References(s => s.Id);

        // CatalogProduct * - 1 Category
        connections.HasRelationship<CatalogProduct, Category>()
            .Using<CatalogProductERP, CategoryERP>()
            .HasForeignKeyOnSource(p => p.CategoryId)
            .References(c => c.Id);

        // CatalogProduct 1 - * InventoryPosition
        connections.HasRelationship<CatalogProduct, InventoryPosition>()
            .Using<CatalogProductERP, InventoryPositionERP>()
            .HasForeignKey(i => i.ProductId)
            .References(p => p.Id);

        // InventoryPosition * - 1 Warehouse
        connections.HasRelationship<InventoryPosition, Warehouse>()
            .Using<InventoryPositionERP, WarehouseERP>()
            .HasForeignKeyOnSource(i => i.WarehouseId)
            .References(w => w.Id);

        // Shipment * - 1 Carrier
        connections.HasRelationship<Shipment, Carrier>()
            .Using<ShipmentERP, CarrierERP>()
            .HasForeignKeyOnSource(s => s.CarrierId)
            .References(c => c.Id);

        // Shipment * - 1 Warehouse
        connections.HasRelationship<Shipment, Warehouse>()
            .Using<ShipmentERP, WarehouseERP>()
            .HasForeignKeyOnSource(s => s.WarehouseId)
            .References(w => w.Id);
    }
}
```

**What matters here:**

- `Property(m => m.Id).To(e => e.Id)` maps a model member to a storage member.
  The two must have identical types; conversions are available on model
  connections (see "Going further" below), not on entity maps.
- `HasRelationship<Customer, SalesOrder>()` configures **both directions** of
  the pair, so `Customer.Orders` and its inverse share one connection. When two
  models are related more than once, pass the navigation:
  `HasRelationship<Order, Customer>(x => x.BillingCustomer)`.
- `HasForeignKey` selects the key on the **target** storage type
  (`Customer 1 - * SalesOrder`); `HasForeignKeyOnSource` selects it on the
  **source** (`SalesOrderLine * - 1 CatalogProduct`).
- Every related model needs an entity map, or `Build()` reports an unmapped
  relationship.

**Register everything once.** `FoundgineModel` is the single registration
point; order does not matter. Create `Domain/SupplyChainModel.cs`:

```csharp
using Foundgine.Core.Semantic.Configuration;

namespace Foundgine.SupplyChain.Domain;

/// <summary>
/// The supply-chain Foundgine model: the single registration point for every
/// semantic, persistence, mapping and connection configuration. Order does not matter.
/// <code>MetadataRegistry registry = new SupplyChainModel().Build();</code>
/// To add to an existing registry use
/// <c>new SupplyChainModel().BuildInto(existingRegistry)</c>.
/// </summary>
public sealed class SupplyChainModel : FoundgineModel
{
    public override void Configure(FoundgineModelBuilder model)
    {
        // Semantic models
        model.Configure<CustomerModelConfiguration>();
        model.Configure<SalesOrderModelConfiguration>();
        model.Configure<SalesOrderLineModelConfiguration>();
        model.Configure<CatalogProductModelConfiguration>();
        model.Configure<SupplierModelConfiguration>();
        model.Configure<CategoryModelConfiguration>();
        model.Configure<InventoryPositionModelConfiguration>();
        model.Configure<WarehouseModelConfiguration>();
        model.Configure<ShipmentModelConfiguration>();
        model.Configure<CarrierModelConfiguration>();

        // Persistence
        model.Configure<CustomerERPConfiguration>();
        model.Configure<SalesOrderERPConfiguration>();
        model.Configure<SalesOrderLineERPConfiguration>();
        model.Configure<CatalogProductERPConfiguration>();
        model.Configure<SupplierERPConfiguration>();
        model.Configure<CategoryERPConfiguration>();
        model.Configure<InventoryPositionERPConfiguration>();
        model.Configure<WarehouseERPConfiguration>();
        model.Configure<ShipmentERPConfiguration>();
        model.Configure<CarrierERPConfiguration>();

        // Model -> storage maps
        model.Configure<CustomerEntityMap>();
        model.Configure<SalesOrderEntityMap>();
        model.Configure<SalesOrderLineEntityMap>();
        model.Configure<CatalogProductEntityMap>();
        model.Configure<SupplierEntityMap>();
        model.Configure<CategoryEntityMap>();
        model.Configure<InventoryPositionEntityMap>();
        model.Configure<WarehouseEntityMap>();
        model.Configure<ShipmentEntityMap>();
        model.Configure<CarrierEntityMap>();

        // Relationship -> foreign key
        model.Configure<SupplyChainConnections>();
    }
}
```

**Checkpoint — build and validate:**

```bash
dotnet build
```

`Build()` itself runs at application startup, not at compile time. It
validates the whole set and throws **one** `InvalidOperationException` listing
every problem it found (a missing key, an unmapped relationship, an ambiguous
connection, mismatched types), so you fix them in one pass. Run the app (step
11) or call `new SupplyChainModel().Build()` from a test to see the result.

---

## 6. Strongly typed handles: `SupplyChainSemantics`

Application code should not hand-build ids like `EntityId.Create("SalesOrderERP")`.
`Domain/SupplyChainSemantics.cs` gives it typed handles — `.Entity`, one field
accessor per mapped member, and `Relationships.<Name>` ids — and **checks every
handle against the compiled registry** the first time its class is used. If you
rename an entity in `ToEntity(...)` or a member in an entity map, the handle
fails fast with a message that says what to update, instead of silently
producing an id the registry has never heard of.

This is the start of the real file (the rest repeats the same pattern for
each entity):

```csharp
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Providers.Aot;

namespace Foundgine.SupplyChain.Domain;

/// <summary>
/// Strongly typed application-facing handles (entity, field and relationship ids) for the
/// Supply Chain sample.
///
/// This replaces the <c>GeneratedSemanticModel</c> type that the AOT source generator used to
/// emit from <c>[FoundgineEntity]</c> / <c>[FoundgineModel]</c> attributes. The sample is now
/// configured fluently (<see cref="SupplyChainModel"/>), so the generator has no declarations to
/// work from and emits nothing. The ids are the same ones the fluent compiler assigns:
/// <list type="bullet">
/// <item><c>EntityId.Create(semanticEntityName)</c>, where the name is the one passed to
/// <c>ToEntity(...)</c> in <c>ModelConfigurations.cs</c>;</item>
/// <item><c>FieldId.Create(semanticEntityName, modelMemberName)</c>;</item>
/// <item><c>RelationshipId.Create(semanticEntityName, navigationMemberName)</c>.</item>
/// </list>
/// Every handle is checked against the compiled registry when its class is first used, so a
/// rename in the fluent configuration fails fast with a clear message instead of silently
/// producing an id the registry has never heard of.
/// </summary>
public static class SupplyChainSemantics
{
    private static readonly MetadataRegistry Registry = new SupplyChainModel().Build();

    private static EntityId EntityOf(string entityName)
    {
        var id = EntityId.Create(entityName);
        if (!Registry.TryGet(id, out _))
            throw new InvalidOperationException(
                $"Semantic entity '{entityName}' is not part of SupplyChainModel. " +
                "Update SupplyChainSemantics to match the ToEntity(...) names in ModelConfigurations.cs.");
        return id;
    }

    private static GeneratedSemanticField FieldOf(EntityId entity, string entityName, string member)
    {
        var id = FieldId.Create(entityName, member);
        if (!Registry.Get(entity).EffectiveFields.Any(f => f.Id == id))
            throw new InvalidOperationException(
                $"Field '{entityName}.{member}' is not mapped in SupplyChainModel. " +
                "Check the model member name and its entity map in Mappings.cs.");
        return new GeneratedSemanticField(entity, id, member);
    }

    private static RelationshipId RelationshipOf(string entityName, string member)
    {
        var id = RelationshipId.Create(entityName, member);
        if (!Registry.Relationships.Any(r => r.Id == id))
            throw new InvalidOperationException(
                $"Relationship '{entityName}.{member}' is not configured in SupplyChainModel. " +
                "Check HasRelationship(...) in ModelConfigurations.cs and the connection in the model.");
        return id;
    }

    private static IReadOnlyList<FieldId> AllFieldsOf(EntityId entity) =>
        Registry.Get(entity).EffectiveFields.Select(f => f.Id).ToArray();

    public static class Customer
    {
        private const string EntityName = "CustomerERP";
        public static readonly EntityId Entity = EntityOf(EntityName);
        public static IReadOnlyList<FieldId> All { get; } = AllFieldsOf(Entity);
    }

    public static class SalesOrder
    {
        private const string EntityName = "SalesOrderERP";
        public static readonly EntityId Entity = EntityOf(EntityName);
        public static readonly GeneratedSemanticField Id = FieldOf(Entity, EntityName, "Id");
    // ... Shipment, InventoryPosition, CatalogProduct, Warehouse, Carrier and
    // Supplier follow the same shape. See Domain/SupplyChainSemantics.cs.
}
```

Earlier versions of this sample had the AOT source generator emit a
`GeneratedSemanticModel` class from `[FoundgineModel]` / `[FoundgineEntity]`
attributes. A fluent model declares nothing for the generator to read, so this
small hand-written class takes its place. It is plain C# over the registry:
there is no extra tooling step.

**Going further — connections with conversions and authorization.** Besides
relationships, the fluent API can describe what a model may _visit_ and who may
follow it, using `FoundgineModelConnectionConfiguration<TModel>`. The sample
doesn't need one, but this is the shape:

```csharp
public sealed class OrderModelConnections : FoundgineModelConnectionConfiguration<Order>
{
    public override void Configure(FoundgineModelConnectionBuilder<Order> connections) =>
        connections.HasConnection<CustomerERP>(x => x.Customer)
            .Maps(x => x.CustomerId, e => e.Id)                       // identical types
            .Maps(x => x.CustomerCode, e => e.CodeNumber, int.Parse)  // type conversion
            .Authorize<UserContext>("CanVisit",
                (user, customer) => user.TenantId == customer.TenantId);
}
```

Register it with `model.Configure<OrderModelConnections>()`. The conversion is
recorded as metadata for the type pair (Foundgine never calls the delegate),
and the `Authorize` lambda is read as an expression tree — member access,
constants, `==`, `!=`, `&&`, `||` and `!` are supported, and anything else is
reported by `Build()`. See `src/csharp/Foundgine.Core/Semantic/Configuration/README.md`
for the full surface.

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
authenticates _and_ is explicitly allowed to run that specific capability
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

**Why this order matters:** authorization runs _before_ any query is built.
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
actual `SemanticOperation` for each read capability using the
`SupplyChainSemantics` handles. Here's the pattern for the first two reads — the rest of your read
methods follow the same shape:

```csharp
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.IR;
using Foundgine.SupplyChain.Application;
using Foundgine.SupplyChain.Domain;

namespace Foundgine.SupplyChain.Infrastructure.Queries;

public sealed class SupplyChainQueryRepository : ISupplyChainQueries
{
    private readonly SemanticSqlQueryExecutor _sql;
    public SupplyChainQueryRepository(SemanticSqlQueryExecutor sql) => _sql = sql;

    public async Task<object> GetOrders(int customerId, CancellationToken ct)
    {
        var operation = Read(
            SupplyChainSemantics.SalesOrder.Entity,
            SupplyChainSemantics.SalesOrder.All,
            SupplyChainSemantics.SalesOrder.CustomerId.Eq(customerId),
            [SupplyChainSemantics.SalesOrder.Id.Asc()]);

        var result = await _sql.ExecuteAsync(operation, ct);
        return new { customerId, orders = result.Rows.Select(x => x.Values).ToArray(), plan = result.Fingerprint };
    }

    public async Task<object> GetOrder(int customerId, int orderId, CancellationToken ct)
    {
        var line = new SemanticReadNode(2, SupplyChainSemantics.SalesOrderLine.Entity,
            SupplyChainSemantics.SalesOrderLine.All, SupplyChainSemantics.SalesOrder.Relationships.Lines, null, []);

        var filter = new SemanticAndFilter([
            SupplyChainSemantics.SalesOrder.Id.Eq(orderId),
            SupplyChainSemantics.SalesOrder.CustomerId.Eq(customerId)]);

        var operation = new SemanticOperation(new SemanticReadNode(1, SupplyChainSemantics.SalesOrder.Entity,
            SupplyChainSemantics.SalesOrder.All, null, null, [line], new SemanticQueryOptions(filter)));

        var result = await _sql.ExecuteAsync(operation, ct);
        var row = result.Rows.FirstOrDefault() ?? throw new KeyNotFoundException("Sales order not found.");
        return new { order = row.Values, lines = result.Rows.Select(x => x.Values).ToArray(), plan = result.Fingerprint };
    }

    // ... GetShipment, ListProducts, ListCustomers, GetProduct, GetInventory,
    // ListSuppliers follow the same "build a SemanticOperation, execute it,
    // shape the response" pattern. Traversals (e.g. Shipment -> Order ->
    // Customer) use SemanticRelationshipFilter with the
    // SupplyChainSemantics.<Model>.Relationships.<Name> constants from
    // step 6.
}
```

**What matters here:**

- `SupplyChainSemantics.SalesOrder.CustomerId.Eq(customerId)` is a
  strongly-typed filter built from the fluent model — you never write a raw
  column name or a WHERE clause.
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

## 9. Wire everything together

Wiring is split in two so `Program.cs` stays short. Foundgine groups related
registrations into a **capability**: a class implementing `IFoundgineCapability`
that you enable with one call. Create `SupplyChainDomain.cs` next to
`Program.cs`; it compiles the fluent model once and registers everything the
application needs:

```csharp
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Runtime;
using Foundgine.SupplyChain.Domain;
using Foundgine.SupplyChain.Application;
using Foundgine.SupplyChain.Infrastructure.Mutations;
using Foundgine.SupplyChain.Infrastructure.Queries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Foundgine.SupplyChain;

/// <summary>
/// Everything the old <c>Application/DependencyInjection.cs</c> and
/// <c>Infrastructure/DependencyInjection.cs</c> used to register (see
/// <c>src/csharp/benchmarks/AgentEndToEnd/Fixtures/SupplyChain.Layered</c> for the layered version this sample
/// grew out of), folded into one capability now that it's a single project: the per-actor
/// <see cref="ICapabilityAuthorizer"/>, the <see cref="SupplyChainApplication"/> facade, the Postgres
/// connection, the metadata registry compiled from the fluent model, and the query/mutation repositories.
///
/// Also supplies the <see cref="Foundgine.Core.Semantic.SemanticModel"/> Foundgine itself needs (via
/// <see cref="FoundgineOptions.UseMetadata"/>) and a permissive Foundgine authorization policy: this
/// sample authorizes at the application layer (actor + token + capability, see
/// <see cref="SupplyChainAuthorizer"/>) rather than through Foundgine's own semantic authorization
/// pipeline, which nothing in this sample's MCP tools currently routes through.
/// </summary>
public sealed class SupplyChainDomain : IFoundgineCapability
{
    /// <summary>Configuration key / environment variable name for the Postgres connection string.</summary>
    public const string ConnectionStringName = "SupplyChainConnectionString";

    // Compiled once from the fluent configuration in Domain/ (SupplyChainModel).
    private static readonly MetadataRegistry Metadata = new SupplyChainModel().Build();

    public static void Configure(FoundgineCapabilityContext context)
    {
        var services = context.Services;

        context.Options.UseMetadata(Metadata);
        context.Options.AuthorizationPolicy ??= new AllowAllSemanticAuthorizationPolicy();

        services.AddSingleton<ICapabilityAuthorizer, SupplyChainAuthorizer>();
        services.AddScoped<SupplyChainApplication>();

        services.AddSingleton(sp =>
            NpgsqlDataSource.Create(ResolveConnectionString(sp.GetRequiredService<IConfiguration>())));

        // Metadata is compiled from the fluent configuration in Domain/ (SupplyChainModel) - it already implements
        // IMetadataProvider and IMetadataCatalog, so there is nothing to wrap here.
        services.AddSingleton<IMetadataProvider>(Metadata);
        services.AddSingleton<IMetadataCatalog>(Metadata);
        services.AddSingleton(Metadata);

        services.AddSingleton<Planner>();
        services.AddSingleton<SemanticSqlQueryExecutor>();
        services.AddScoped<ISupplyChainQueries, SupplyChainQueryRepository>();
        services.AddScoped<ISupplyChainMutations, SupplyChainMutationRepository>();
    }

    private static string ResolveConnectionString(IConfiguration configuration) =>
        configuration[ConnectionStringName]
        ?? Environment.GetEnvironmentVariable(ConnectionStringName)
        ?? throw new InvalidOperationException($"{ConnectionStringName} is required.");
}

/// <summary>Fluent <c>Use</c> surface for <see cref="SupplyChainDomain"/>.</summary>
public static class SupplyChainDomainFoundgineOptionsExtensions
{
    /// <summary>Enables <see cref="SupplyChainDomain"/>. Equivalent to <c>options.Enable&lt;SupplyChainDomain&gt;()</c>.</summary>
    public static FoundgineOptions UseSupplyChainDomain(this FoundgineOptions options) =>
        options.Enable<SupplyChainDomain>();
}
```

`SupplyChainDomain.Metadata` is `new SupplyChainModel().Build()`. The resulting
`MetadataRegistry` already implements `IMetadataProvider` and
`IMetadataCatalog`, so it is registered as-is — there is nothing to wrap.

Now `Program.cs` only has to enable the capability and host the MCP tools:

```csharp
using Foundgine.SupplyChain;
using Foundgine.SupplyChain.Application;
using ModelContextProtocol.Server;
using Foundgine.Providers.Tools.MCP;
using Foundgine.Runtime;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// --- everything the old Application/DependencyInjection.cs + Infrastructure/DependencyInjection.cs
//     did, in one place, since there's only one project to wire up now. ---
builder.Services.AddFoundgine(options =>
{
    // Starter path: only the domain model + core Foundgine execution boundary.
    // Optional capabilities are deliberately opt-in.
    options.UseSupplyChainDomain();
});

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

// Every tool requires a 'token' proving the caller actually controls the identity
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

    [McpServerTool(Name = "describe_capabilities")]
    public Task<object> Describe(string actor, string token) =>
        With(a => Task.FromResult(a.DescribeCapabilities(actor, token)));

    [McpServerTool(Name = "get_my_orders")]
    public Task<object> GetMyOrders(string actor, string token, int customerId, CancellationToken ct = default) =>
        With(a => a.GetMyOrders(actor, token, customerId, ct));

    [McpServerTool(Name = "get_order")]
    public Task<object> GetOrder(string actor, string token, int customerId, int orderId,
        CancellationToken ct = default) => With(a => a.GetOrder(actor, token, customerId, orderId, ct));

    [McpServerTool(Name = "get_shipment")]
    public Task<object> GetShipment(string actor, string token, int customerId, int shipmentId,
        CancellationToken ct = default) => With(a => a.GetShipment(actor, token, customerId, shipmentId, ct));

    [McpServerTool(Name = "list_products")]
    public Task<object> ListProducts(string actor, string token, CancellationToken ct = default) =>
        With(a => a.ListProducts(actor, token, ct));

    [McpServerTool(Name = "list_customers")]
    public Task<object> ListCustomers(string actor, string token, CancellationToken ct = default) =>
        With(a => a.ListCustomers(actor, token, ct));

    [McpServerTool(Name = "get_product")]
    public Task<object> GetProduct(string actor, string token, int productId, CancellationToken ct = default) =>
        With(a => a.GetProduct(actor, token, productId, ct));

    [McpServerTool(Name = "get_inventory")]
    public Task<object> GetInventory(string actor, string token, int productId, CancellationToken ct = default) =>
        With(a => a.GetInventory(actor, token, productId, ct));

    [McpServerTool(Name = "list_suppliers")]
    public Task<object> ListSuppliers(string actor, string token, CancellationToken ct = default) =>
        With(a => a.ListSuppliers(actor, token, ct));

    [McpServerTool(Name = "update_inventory")]
    public Task<object> UpdateInventory(string actor, string token, int warehouseId, int productId, int quantity,
        CancellationToken ct = default) =>
        With(a => a.UpdateInventory(actor, token, warehouseId, productId, quantity, ct));

    [McpServerTool(Name = "create_shipment")]
    public Task<object> CreateShipment(string actor, string token, int orderId, int carrierId, int warehouseId,
        string trackingNumber, CancellationToken ct = default) => With(a =>
        a.CreateShipment(actor, token, orderId, carrierId, warehouseId, trackingNumber, ct));

    [McpServerTool(Name = "update_shipment")]
    public Task<object> UpdateShipment(string actor, string token, int shipmentId, string status,
        CancellationToken ct = default) => With(a => a.UpdateShipment(actor, token, shipmentId, status, ct));

    [McpServerTool(Name = "place_order")]
    public Task<object> PlaceOrder(string actor, string token, int customerId, OrderLine[] lines, string idempotencyKey,
        CancellationToken ct = default) => With(a => a.PlaceOrder(actor, token, customerId, lines, idempotencyKey, ct));

    [McpServerTool(Name = "cancel_order")]
    public Task<object> CancelOrder(string actor, string token, int customerId, int orderId,
        CancellationToken ct = default) => With(a => a.CancelOrder(actor, token, customerId, orderId, ct));
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
