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
    [FoundgineField("Id", StorageName = "order_id", Id = 1, IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField("CustomerId", StorageName = "customer_id", Id = 2)]
    public int CustomerId { get; init; }

    [FoundgineField("Status", StorageName = "status", Id = 3)]
    public string Status { get; init; } = "";

    [FoundgineField("TotalAmount", StorageName = "total_amount", Id = 4)]
    public decimal TotalAmount { get; init; }

    [FoundgineRelationship(typeof(SalesOrderLineERP), "OrderId", "Id", Id = 2, Name = "Lines")]
    public IReadOnlyList<SalesOrderLineERP> Lines { get; init; } = [];

    [FoundgineRelationship(typeof(ShipmentERP), "OrderId", "Id", Id = 8, Name = "Shipments")]
    public IReadOnlyList<ShipmentERP> Shipments { get; init; } = [];
}

[FoundgineEntity("SalesOrderLineERP", StorageName = "order_items", Id = 3)]
public sealed class SalesOrderLineERP
{
    [FoundgineField("Id", StorageName = "order_item_id", Id = 1, IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField("OrderId", StorageName = "order_id", Id = 2)]
    public int OrderId { get; init; }

    [FoundgineField("ProductId", StorageName = "product_id", Id = 3)]
    public int ProductId { get; init; }

    [FoundgineField("Quantity", StorageName = "quantity", Id = 4)]
    public int Quantity { get; init; }

    [FoundgineField("UnitPrice", StorageName = "unit_price", Id = 5)]
    public decimal UnitPrice { get; init; }

    [FoundgineRelationship(typeof(CatalogProductERP), "ProductId", "Id", Id = 3, Name = "Product")]
    public CatalogProductERP Product { get; init; } = null!;
}

[FoundgineEntity("CatalogProductERP", StorageName = "products", Id = 4)]
public sealed class CatalogProductERP
{
    [FoundgineField("Id", StorageName = "product_id", Id = 1, IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField("Name", StorageName = "product_name", Id = 2)]
    public string Name { get; init; } = "";

    [FoundgineField("Sku", StorageName = "sku", Id = 3)]
    public string Sku { get; init; } = "";

    [FoundgineField("UnitPrice", StorageName = "unit_price", Id = 4)]
    public decimal UnitPrice { get; init; }

    [FoundgineRelationship(typeof(SupplierERP), "SupplierId", "Id", Id = 4, Name = "Supplier")]
    public SupplierERP Supplier { get; init; } = null!;

    [FoundgineRelationship(typeof(CategoryERP), "CategoryId", "Id", Id = 5, Name = "Category")]
    public CategoryERP Category { get; init; } = null!;

    [FoundgineRelationship(typeof(InventoryPositionERP), "ProductId", "Id", Id = 6, Name = "InventoryPositions")]
    public IReadOnlyList<InventoryPositionERP> InventoryPositions { get; init; } = [];

    [FoundgineField("SupplierId", StorageName = "supplier_id", Id = 5)]
    public int SupplierId { get; init; }

    [FoundgineField("CategoryId", StorageName = "category_id", Id = 6)]
    public int CategoryId { get; init; }
}

[FoundgineEntity("SupplierERP", StorageName = "suppliers", Id = 5)]
public sealed class SupplierERP
{
    [FoundgineField("Id", StorageName = "supplier_id", Id = 1, IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField("Name", StorageName = "supplier_name", Id = 2)]
    public string Name { get; init; } = "";

    [FoundgineField("Email", StorageName = "email", Id = 3)]
    public string Email { get; init; } = "";
}

[FoundgineEntity("CategoryERP", StorageName = "categories", Id = 6)]
public sealed class CategoryERP
{
    [FoundgineField("Id", StorageName = "category_id", Id = 1, IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField("Name", StorageName = "category_name", Id = 2)]
    public string Name { get; init; } = "";
}

[FoundgineEntity("InventoryPositionERP", StorageName = "inventory", Id = 7)]
public sealed class InventoryPositionERP
{
    [FoundgineField("Id", StorageName = "inventory_id", Id = 1, IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField("WarehouseId", StorageName = "warehouse_id", Id = 2)]
    public int WarehouseId { get; init; }

    [FoundgineField("ProductId", StorageName = "product_id", Id = 3)]
    public int ProductId { get; init; }

    [FoundgineField("QuantityOnHand", StorageName = "quantity_on_hand", Id = 4)]
    public int QuantityOnHand { get; init; }

    [FoundgineField("ReorderLevel", StorageName = "reorder_level", Id = 5)]
    public int ReorderLevel { get; init; }

    [FoundgineRelationship(typeof(WarehouseERP), "WarehouseId", "Id", Id = 7, Name = "Warehouse")]
    public WarehouseERP Warehouse { get; init; } = null!;
}

[FoundgineEntity("WarehouseERP", StorageName = "warehouses", Id = 8)]
public sealed class WarehouseERP
{
    [FoundgineField("Id", StorageName = "warehouse_id", Id = 1, IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField("Name", StorageName = "warehouse_name", Id = 2)]
    public string Name { get; init; } = "";

    [FoundgineField("Location", StorageName = "location", Id = 3)]
    public string Location { get; init; } = "";
}

[FoundgineEntity("ShipmentERP", StorageName = "shipments", Id = 9)]
public sealed class ShipmentERP
{
    [FoundgineField("Id", StorageName = "shipment_id", Id = 1, IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField("OrderId", StorageName = "order_id", Id = 2)]
    public int OrderId { get; init; }

    [FoundgineField("CarrierId", StorageName = "carrier_id", Id = 3)]
    public int CarrierId { get; init; }

    [FoundgineField("WarehouseId", StorageName = "warehouse_id", Id = 4)]
    public int WarehouseId { get; init; }

    [FoundgineField("TrackingNumber", StorageName = "tracking_number", Id = 5)]
    public string TrackingNumber { get; init; } = "";

    [FoundgineField("Status", StorageName = "shipping_status", Id = 6)]
    public string Status { get; init; } = "";

    [FoundgineRelationship(typeof(SalesOrderERP), "OrderId", "Id", Id = 11, Name = "Order")]
    public SalesOrderERP Order { get; init; } = null!;

    [FoundgineRelationship(typeof(CarrierERP), "CarrierId", "Id", Id = 9, Name = "Carrier")]
    public CarrierERP Carrier { get; init; } = null!;

    [FoundgineRelationship(typeof(WarehouseERP), "WarehouseId", "Id", Id = 10, Name = "Warehouse")]
    public WarehouseERP Warehouse { get; init; } = null!;
}

[FoundgineEntity("CarrierERP", StorageName = "carriers", Id = 10)]
public sealed class CarrierERP
{
    [FoundgineField("Id", StorageName = "carrier_id", Id = 1, IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField("Name", StorageName = "carrier_name", Id = 2)]
    public string Name { get; init; } = "";
}