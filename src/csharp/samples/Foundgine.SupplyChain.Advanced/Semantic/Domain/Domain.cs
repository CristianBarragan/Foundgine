using Foundgine.Providers.Aot;
using Foundgine.Core.Semantic;

namespace Foundgine.SupplyChain.Advanced.Domain;

public readonly record struct CompanyId(int Value);

public readonly record struct BusinessUnitId(int Value);

public readonly record struct WarehouseId(int Value);

public readonly record struct SupplierId(int Value);

public readonly record struct SupplierSiteId(int Value);

public readonly record struct CertificationId(int Value);

public readonly record struct ProductId(int Value);

public readonly record struct PurchaseOrderId(int Value);

public readonly record struct PurchaseOrderLineId(int Value);

public readonly record struct ShipmentId(int Value);

public readonly record struct InventoryLotId(int Value);

public readonly record struct CustomerOrderId(int Value);

public readonly record struct CustomerOrderLineId(int Value);

/// <summary>Domain types are also the CLR source observed by the AOT metadata producer.</summary>
[FoundgineEntity("Company", StorageName = "companies", Id = 1000)]
public sealed record Company(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    CompanyId Id,
    string Name,
    [property: FoundgineSemanticDimension("tenant")]
    string TenantId);

[SemanticEntity]
[FoundgineEntity("BusinessUnit", StorageName = "business_units", Id = 1001)]
public sealed record BusinessUnit(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    BusinessUnitId Id,
    [property: FoundgineSemanticDimension("company"), FoundgineField(Index = true)]
    CompanyId CompanyId,
    string Name);

[SemanticEntity]
[FoundgineEntity("Warehouse", StorageName = "warehouses", Id = 1002)]
public sealed record Warehouse(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    WarehouseId Id,
    [property: FoundgineSemanticDimension("businessUnit"), FoundgineField(Index = true)]
    BusinessUnitId BusinessUnitId,
    string Name,
    [property: FoundgineSemanticDimension("tenant")]
    string TenantId)
{
    // Relationship ids are no longer assigned manually: when Id is omitted the
    // AOT generator derives a stable id from a hash of "Entity.Property", so
    // reordering members, merging modules, or adding new relationships
    // elsewhere never forces renumbering or risks a silent collision.
    [FoundgineRelationship(typeof(BusinessUnit), "BusinessUnitId", "Id", Name = "businessUnit")]
    public BusinessUnit BusinessUnit { get; init; } = null!;

    [FoundgineRelationship(typeof(InventoryLot), "WarehouseId", "Id", Name = "inventory")]
    public IReadOnlyList<InventoryLot> Inventory { get; init; } = [];
}

[SemanticEntity]
[FoundgineEntity("Supplier", StorageName = "suppliers", Id = 1003)]
[FoundgineAlias("Vendor", Weight = 95)]
[FoundgineAlias("Seller", Weight = 90)]
public sealed record Supplier(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    SupplierId Id,
    string Name,
    [property: FoundgineSemanticDimension("country"), FoundgineField(Index = true),
               FoundgineAlias("State", Weight = 85)]
    string Country,
    decimal RiskScore,
    [property: FoundgineSemanticDimension("tenant")]
    string TenantId)
{
    [FoundgineRelationship(typeof(SupplierCertification), "SupplierId", "Id", Name = "certifications")]
    public IReadOnlyList<SupplierCertification> Certifications { get; init; } = [];

    [FoundgineRelationship(typeof(ComplianceIncident), "SupplierId", "Id", Name = "incidents")]
    public IReadOnlyList<ComplianceIncident> Incidents { get; init; } = [];

    // Reverse semantic relationship (recommendation 3): lets a query start
    // from a supplier and ask "which purchase orders depend on this
    // supplier?" without the caller having to know PurchaseOrder owns the
    // foreign key.
    [FoundgineRelationship(typeof(PurchaseOrder), "SupplierId", "Id", Name = "purchaseOrders")]
    public IReadOnlyList<PurchaseOrder> PurchaseOrders { get; init; } = [];
}

[FoundgineEntity("SupplierSite", StorageName = "supplier_sites", Id = 1004)]
public sealed record SupplierSite(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    SupplierSiteId Id,
    [property: FoundgineField(Index = true)]
    SupplierId SupplierId,
    [property: FoundgineSemanticDimension("country")]
    string Country,
    string Name);

[SemanticEntity]
[FoundgineEntity("SupplierCertification", StorageName = "supplier_certifications", Id = 1005)]
public sealed record SupplierCertification(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    CertificationId Id,
    [property: FoundgineField(Index = true)]
    SupplierId SupplierId,
    string Type,
    DateOnly ValidFrom,
    DateOnly ValidTo);

[SemanticEntity]
[FoundgineEntity("Product", StorageName = "products", Id = 1006)]
public sealed record Product(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    ProductId Id,
    string Sku,
    string Name,
    [property: FoundgineSemanticDimension("category"), FoundgineField(Index = true)]
    string Category,
    decimal SafetyStock)
{
    [FoundgineRelationship(typeof(ProductComponent), "ParentProductId", "Id", Name = "components")]
    public IReadOnlyList<ProductComponent> Components { get; init; } = [];

    [FoundgineRelationship(typeof(PurchaseOrderLine), "ProductId", "Id", Name = "purchaseOrderLines")]
    public IReadOnlyList<PurchaseOrderLine> PurchaseOrderLines { get; init; } = [];

    // Reverse semantic relationship (recommendation 3): "where is this used?"
    // - every BOM line that consumes this product as a component.
    [FoundgineRelationship(typeof(ProductComponent), "ComponentProductId", "Id", Name = "usedInComponents")]
    public IReadOnlyList<ProductComponent> UsedInComponents { get; init; } = [];
}

[SemanticEntity]
[FoundgineEntity("ProductComponent", StorageName = "product_components", Id = 1007)]
public sealed record ProductComponent(
    [property: FoundgineField("ParentProductId", Id = 1, IsPrimaryKey = true)]
    ProductId ParentProductId,
    [property: FoundgineField(Index = true)]
    ProductId ComponentProductId,
    decimal QuantityPerParent,
    // BOM metadata (recommendation 4): even where unused today, these are the
    // fields a manufacturing/planning engine eventually needs from a BOM line.
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    string Revision = "1",
    bool IsPhantom = false,
    decimal YieldLossPercent = 0m,
    decimal ScrapFactor = 0m)
{
    [FoundgineRelationship(typeof(Product), "ComponentProductId", "Id", Name = "componentProduct")]
    public Product ComponentProduct { get; init; } = null!;
}

public enum PurchaseOrderStatus
{
    Open,
    PartiallyReceived,
    Cancelled,
    Closed
}

[SemanticEntity]
[FoundgineEntity("PurchaseOrder", StorageName = "purchase_orders", Id = 1008)]
[FoundgineAlias("PO", Weight = 100)]
[FoundgineAlias("POs", Weight = 95)]
[FoundgineAlias("Buy", Weight = 90)]
[FoundgineAlias("Buys", Weight = 85)]
public sealed record PurchaseOrder(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    PurchaseOrderId Id,
    [property: FoundgineField(Index = true)]
    SupplierId SupplierId,
    [property: FoundgineField(Index = true)]
    WarehouseId WarehouseId,
    PurchaseOrderStatus Status,
    DateOnly OrderedOn,
    [property: FoundgineAlias("DueDate", Weight = 90)]
    DateOnly ExpectedArrival)
{
    [FoundgineRelationship(typeof(PurchaseOrderLine), "PurchaseOrderId", "Id", Name = "lines")]
    public IReadOnlyList<PurchaseOrderLine> Lines { get; init; } = [];

    [FoundgineRelationship(typeof(Shipment), "PurchaseOrderId", "Id", Name = "shipments")]
    public IReadOnlyList<Shipment> Shipments { get; init; } = [];

    [FoundgineRelationship(typeof(Supplier), "SupplierId", "Id", Name = "supplier")]
    [FoundgineAlias("vendor", Weight = 85)]
    public Supplier Supplier { get; init; } = null!;
}

[SemanticEntity]
[FoundgineEntity("PurchaseOrderLine", StorageName = "purchase_order_lines", Id = 1009)]
public sealed record PurchaseOrderLine(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    PurchaseOrderLineId Id,
    PurchaseOrderId PurchaseOrderId,
    [property: FoundgineField(Index = true)]
    ProductId ProductId,
    decimal Quantity,
    decimal UnitPrice)
{
    [FoundgineRelationship(typeof(PurchaseOrder), "PurchaseOrderId", "Id", Name = "purchaseOrder")]
    public PurchaseOrder PurchaseOrder { get; init; } = null!;
}

public enum ShipmentStatus
{
    Planned,
    InTransit,
    Delayed,
    PartiallyReceived,
    Received,
    Cancelled
}

[SemanticEntity]
[FoundgineEntity("Shipment", StorageName = "shipments", Id = 1010)]
public sealed record Shipment(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    ShipmentId Id,
    PurchaseOrderId PurchaseOrderId,
    DateOnly ExpectedArrival,
    DateOnly? ActualArrival,
    ShipmentStatus Status,
    decimal Quantity);

[SemanticEntity]
[FoundgineEntity("InventoryLot", StorageName = "inventory", Id = 1011)]
public sealed record InventoryLot(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    InventoryLotId Id,
    WarehouseId WarehouseId,
    [property: FoundgineField(Index = true)]
    ProductId ProductId,
    decimal OnHand,
    decimal Reserved,
    decimal Quarantined,
    DateOnly ReceivedOn)
{
    [FoundgineRelationship(typeof(Warehouse), "WarehouseId", "Id", Name = "warehouse")]
    public Warehouse Warehouse { get; init; } = null!;
}

[SemanticEntity]
[FoundgineEntity("CustomerOrder", StorageName = "customer_orders", Id = 1012)]
public sealed record CustomerOrder(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    CustomerOrderId Id,
    BusinessUnitId BusinessUnitId,
    DateOnly PlacedOn,
    string Status)
{
    [FoundgineRelationship(typeof(CustomerOrderLine), "CustomerOrderId", "Id", Name = "lines")]
    public IReadOnlyList<CustomerOrderLine> Lines { get; init; } = [];
}

[SemanticEntity]
[FoundgineEntity("CustomerOrderLine", StorageName = "customer_order_lines", Id = 1013)]
public sealed record CustomerOrderLine(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    CustomerOrderLineId Id,
    CustomerOrderId CustomerOrderId,
    ProductId ProductId,
    decimal Quantity);

// Event entities (recommendation 5): each of these records something that
// happened at a point in time and is never mutated afterward, as opposed to
// the state entities above (Product, Supplier, InventoryLot, ...) whose rows
// are updated in place. [FoundgineEvent] marks that distinction in metadata
// and, where given a field name, points at the column a temporal/"as of"
// query should key on.
[FoundgineEntity("InventoryMovement", StorageName = "inventory_movements", Id = 1015)]
[FoundgineEvent("OccurredAt")]
public sealed record InventoryMovement(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    int Id,
    [property: FoundgineField(Index = true)]
    InventoryLotId LotId,
    decimal Quantity,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed record Allocation(CustomerOrderLineId OrderLineId, InventoryLotId LotId, decimal Quantity);

public sealed record ProductionOrder(int Id, ProductId ProductId, decimal Quantity, string Status);

public sealed record ProductionMaterial(int Id, int ProductionOrderId, ProductId ProductId, decimal Quantity);

[FoundgineEntity("ProductionOutput", StorageName = "production_outputs", Id = 1016)]
[FoundgineEvent]
public sealed record ProductionOutput(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    int Id,
    int ProductionOrderId,
    ProductId ProductId,
    decimal Quantity);

[SemanticEntity]
[FoundgineEntity("ComplianceIncident", StorageName = "compliance_incidents", Id = 1014)]
[FoundgineEvent("OccurredOn")]
public sealed record ComplianceIncident(
    [property: FoundgineField("Id", Id = 1, IsPrimaryKey = true)]
    int Id,
    [property: FoundgineField(Index = true)]
    SupplierId SupplierId,
    string Severity,
    DateOnly OccurredOn,
    string Description);