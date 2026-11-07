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
