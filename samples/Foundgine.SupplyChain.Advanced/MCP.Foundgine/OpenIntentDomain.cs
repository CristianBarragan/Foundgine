using Foundgine.Providers.Aot;

namespace Foundgine.SupplyChain.Advanced.OpenIntent.Domain;

/// <summary>
/// CLR source model for the open-intent sample. Foundgine's Roslyn generator
/// turns these declarations into immutable structural metadata at build time.
/// This file is the source model; it is not a hand-built runtime semantic model.
/// </summary>
[FoundgineEntity("Customer", StorageName = "customers")]
public sealed record Customer(
[property: FoundgineField("Id", StorageName = "customer_id", IsPrimaryKey = true)] int Id,
[property: FoundgineAlias("given name")] string FirstName,
[property: FoundgineAlias("family name")] string LastName,
string Email,
string Phone,
string ShippingAddress)
{
[FoundgineRelationship(typeof(Order), "CustomerId", "Id", Name = "orders")]
public IReadOnlyList<Order> Orders { get; init; } = [];
}

[FoundgineEntity("Order", StorageName = "orders")]
public sealed record Order(
[property: FoundgineField("Id", StorageName = "order_id", IsPrimaryKey = true)] int Id,
[property: FoundgineField(StorageName = "customer_id")] int CustomerId,
[property: FoundgineField(StorageName = "order_date")] DateTimeOffset OrderDate,
string Status,
decimal TotalAmount)
{
[FoundgineRelationship(typeof(Customer), "CustomerId", "Id", Name = "customer")]
public Customer Customer { get; init; } = null!;


[FoundgineRelationship(typeof(OrderItem), "OrderId", "Id", Name = "items")]
public IReadOnlyList<OrderItem> Items { get; init; } = [];

[FoundgineRelationship(typeof(Shipment), "OrderId", "Id", Name = "shipments")]
public IReadOnlyList<Shipment> Shipments { get; init; } = [];


}

[FoundgineEntity("OrderItem", StorageName = "order_items")]
public sealed record OrderItem(
[property: FoundgineField("Id", StorageName = "order_item_id", IsPrimaryKey = true)] int Id,
[property: FoundgineField(StorageName = "order_id")] int OrderId,
[property: FoundgineField(StorageName = "product_id")] int ProductId,
int Quantity,
decimal UnitPrice)
{
[FoundgineRelationship(typeof(Order), "OrderId", "Id", Name = "order")]
public Order Order { get; init; } = null!;


[FoundgineRelationship(typeof(Product), "ProductId", "Id", Name = "product")]
public Product Product { get; init; } = null!;


}

[FoundgineEntity("Product", StorageName = "products")]
[FoundgineAlias("item")]
[FoundgineAlias("catalog item")]
public sealed record Product(
[property: FoundgineField("Id", StorageName = "product_id", IsPrimaryKey = true)] int Id,
[property: FoundgineAlias("product name")] string Name,
string Sku,
decimal UnitPrice,
int SupplierId,
int CategoryId)
{
[FoundgineRelationship(typeof(Supplier), "SupplierId", "Id", Name = "supplier")]
public Supplier Supplier { get; init; } = null!;


[FoundgineRelationship(typeof(Category), "CategoryId", "Id", Name = "category")]
public Category Category { get; init; } = null!;

[FoundgineRelationship(typeof(Inventory), "ProductId", "Id", Name = "inventory")]
public IReadOnlyList<Inventory> Inventory { get; init; } = [];


}

[FoundgineEntity("Supplier", StorageName = "suppliers")]
[FoundgineAlias("vendor", Weight = 95)]
[FoundgineAlias("seller", Weight = 90)]
public sealed record Supplier(
[property: FoundgineField("Id", StorageName = "supplier_id", IsPrimaryKey = true)] int Id,
string Name,
string Email,
string Phone,
string Address,
[property: FoundgineAlias("region")] string State,
decimal TotalOrderValue,
decimal? NegotiatedCost)
{
[FoundgineRelationship(typeof(Product), "SupplierId", "Id", Name = "products")]
public IReadOnlyList<Product> Products { get; init; } = [];


[FoundgineRelationship(typeof(PurchaseOrder), "SupplierId", "Id", Name = "purchase orders")]
public IReadOnlyList<PurchaseOrder> PurchaseOrders { get; init; } = [];


}

[FoundgineEntity("Category", StorageName = "categories")]
public sealed record Category(
[property: FoundgineField("Id", StorageName = "category_id", IsPrimaryKey = true)] int Id,
[property: FoundgineAlias("category name")] string Name,
string Description)
{
[FoundgineRelationship(typeof(Product), "CategoryId", "Id", Name = "products")]
public IReadOnlyList<Product> Products { get; init; } = [];
}

[FoundgineEntity("Inventory", StorageName = "inventory")]
public sealed record Inventory(
[property: FoundgineField("Id", StorageName = "inventory_id", IsPrimaryKey = true)] int Id,
int WarehouseId,
int ProductId,
int QuantityOnHand,
int ReorderLevel,
DateTimeOffset LastUpdated)
{
[FoundgineRelationship(typeof(Warehouse), "WarehouseId", "Id", Name = "warehouse")]
public Warehouse Warehouse { get; init; } = null!;


[FoundgineRelationship(typeof(Product), "ProductId", "Id", Name = "product")]
public Product Product { get; init; } = null!;


}

[FoundgineEntity("Warehouse", StorageName = "warehouses")]
public sealed record Warehouse(
[property: FoundgineField("Id", StorageName = "warehouse_id", IsPrimaryKey = true)] int Id,
string Name,
string Location,
int CapacityM3)
{
[FoundgineRelationship(typeof(Inventory), "WarehouseId", "Id", Name = "inventory")]
public IReadOnlyList<Inventory> Inventory { get; init; } = [];


[FoundgineRelationship(typeof(Shipment), "WarehouseId", "Id", Name = "shipments")]
public IReadOnlyList<Shipment> Shipments { get; init; } = [];


}

[FoundgineEntity("Shipment", StorageName = "shipments")]
public sealed record Shipment(
[property: FoundgineField("Id", StorageName = "shipment_id", IsPrimaryKey = true)] int Id,
int OrderId,
int CarrierId,
int WarehouseId,
string TrackingNumber,
DateTimeOffset? ShipmentDate,
DateOnly? EstimatedDelivery,
DateOnly? ActualDelivery,
string Status)
{
[FoundgineRelationship(typeof(Order), "OrderId", "Id", Name = "order")]
public Order Order { get; init; } = null!;


[FoundgineRelationship(typeof(Carrier), "CarrierId", "Id", Name = "carrier")]
public Carrier Carrier { get; init; } = null!;

[FoundgineRelationship(typeof(Warehouse), "WarehouseId", "Id", Name = "warehouse")]
public Warehouse Warehouse { get; init; } = null!;


}

[FoundgineEntity("Carrier", StorageName = "carriers")]
public sealed record Carrier(
[property: FoundgineField("Id", StorageName = "carrier_id", IsPrimaryKey = true)] int Id,
string Name,
string TrackingUrlTemplate,
string ContactPhone)
{
[FoundgineRelationship(typeof(Shipment), "CarrierId", "Id", Name = "shipments")]
public IReadOnlyList<Shipment> Shipments { get; init; } = [];
}

[FoundgineEntity("PurchaseOrder", StorageName = "purchase_orders")]
[FoundgineAlias("PO", Weight = 100)]
[FoundgineAlias("purchase", Weight = 90)]
public sealed record PurchaseOrder(
[property: FoundgineField("Id", StorageName = "purchase_order_id", IsPrimaryKey = true)] int Id,
int SupplierId,
DateOnly ExpectedDate,
DateOnly? ReceivedDate,
string Status)
{
[FoundgineRelationship(typeof(Supplier), "SupplierId", "Id", Name = "supplier")]
public Supplier Supplier { get; init; } = null!;
}
