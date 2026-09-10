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
public sealed class Supplier
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
}

[FoundgineModel("Category", Id = 106)]
public sealed class Category
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

[FoundgineModel("InventoryPosition", Id = 107)]
public sealed class InventoryPosition
{
    public int Id { get; init; }
    public int WarehouseId { get; init; }
    public int ProductId { get; init; }
    public int QuantityOnHand { get; init; }
    public int ReorderLevel { get; init; }
}

[FoundgineModel("Warehouse", Id = 108)]
public sealed class Warehouse
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Location { get; init; } = "";
}

[FoundgineModel("Shipment", Id = 109)]
public sealed class Shipment
{
    public int Id { get; init; }
    public int OrderId { get; init; }
    public int CarrierId { get; init; }
    public int WarehouseId { get; init; }
    public string TrackingNumber { get; init; } = "";
    public string Status { get; init; } = "";
}

[FoundgineModel("Carrier", Id = 110)]
public sealed class Carrier
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}