namespace Foundgine.SupplyChain.Advanced.Domain;

// Plain domain types: no Foundgine attributes and no Foundgine reference. Semantic
// meaning, storage and the mapping between them are configured in
// Semantics/Configuration and registered by SupplyChainModel.

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

public sealed record Company(
    CompanyId Id,
    string Name,
    string TenantId);

public sealed record BusinessUnit(
    BusinessUnitId Id,
    CompanyId CompanyId,
    string Name);

public sealed record Warehouse(
    WarehouseId Id,
    BusinessUnitId BusinessUnitId,
    string Name,
    string TenantId)
{
    public BusinessUnit BusinessUnit { get; init; } = null!;

    public IReadOnlyList<InventoryLot> Inventory { get; init; } = [];
}

public sealed record Supplier(
    SupplierId Id,
    string Name,
    string Country,
    decimal RiskScore,
    string TenantId)
{
    public IReadOnlyList<SupplierCertification> Certifications { get; init; } = [];

    public IReadOnlyList<ComplianceIncident> Incidents { get; init; } = [];

    // Reverse semantic relationship (recommendation 3): lets a query start
    // from a supplier and ask "which purchase orders depend on this
    // supplier?" without the caller having to know PurchaseOrder owns the
    // foreign key.
    public IReadOnlyList<PurchaseOrder> PurchaseOrders { get; init; } = [];
}

public sealed record SupplierSite(
    SupplierSiteId Id,
    SupplierId SupplierId,
    string Country,
    string Name);

public sealed record SupplierCertification(
    CertificationId Id,
    SupplierId SupplierId,
    string Type,
    DateOnly ValidFrom,
    DateOnly ValidTo);

public sealed record Product(
    ProductId Id,
    string Sku,
    string Name,
    string Category,
    decimal SafetyStock)
{
    public IReadOnlyList<ProductComponent> Components { get; init; } = [];

    public IReadOnlyList<PurchaseOrderLine> PurchaseOrderLines { get; init; } = [];

    // Reverse semantic relationship (recommendation 3): "where is this used?"
    // - every BOM line that consumes this product as a component.
    public IReadOnlyList<ProductComponent> UsedInComponents { get; init; } = [];
}

public sealed record ProductComponent(
    ProductId ParentProductId,
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
    public Product ComponentProduct { get; init; } = null!;
}

public enum PurchaseOrderStatus
{
    Open,
    PartiallyReceived,
    Cancelled,
    Closed
}

public sealed record PurchaseOrder(
    PurchaseOrderId Id,
    SupplierId SupplierId,
    WarehouseId WarehouseId,
    PurchaseOrderStatus Status,
    DateOnly OrderedOn,
    DateOnly ExpectedArrival)
{
    public IReadOnlyList<PurchaseOrderLine> Lines { get; init; } = [];

    public IReadOnlyList<Shipment> Shipments { get; init; } = [];

    public Supplier Supplier { get; init; } = null!;
}

public sealed record PurchaseOrderLine(
    PurchaseOrderLineId Id,
    PurchaseOrderId PurchaseOrderId,
    ProductId ProductId,
    decimal Quantity,
    decimal UnitPrice)
{
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

public sealed record Shipment(
    ShipmentId Id,
    PurchaseOrderId PurchaseOrderId,
    DateOnly ExpectedArrival,
    DateOnly? ActualArrival,
    ShipmentStatus Status,
    decimal Quantity);

public sealed record InventoryLot(
    InventoryLotId Id,
    WarehouseId WarehouseId,
    ProductId ProductId,
    decimal OnHand,
    decimal Reserved,
    decimal Quarantined,
    DateOnly ReceivedOn)
{
    public Warehouse Warehouse { get; init; } = null!;
}

public sealed record CustomerOrder(
    CustomerOrderId Id,
    BusinessUnitId BusinessUnitId,
    DateOnly PlacedOn,
    string Status)
{
    public IReadOnlyList<CustomerOrderLine> Lines { get; init; } = [];
}

public sealed record CustomerOrderLine(
    CustomerOrderLineId Id,
    CustomerOrderId CustomerOrderId,
    ProductId ProductId,
    decimal Quantity);

// Event entities (recommendation 5): each of these records something that
// happened at a point in time and is never mutated afterward, as opposed to
// the state entities above (Product, Supplier, InventoryLot, ...) whose rows
// are updated in place. The model configuration marks the distinction with
// IsEvent(...) and, where given a member, the column a temporal/"as of"
// query should key on.
public sealed record InventoryMovement(
    int Id,
    InventoryLotId LotId,
    decimal Quantity,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed record Allocation(CustomerOrderLineId OrderLineId, InventoryLotId LotId, decimal Quantity);

public sealed record ProductionOrder(int Id, ProductId ProductId, decimal Quantity, string Status);

public sealed record ProductionMaterial(int Id, int ProductionOrderId, ProductId ProductId, decimal Quantity);

public sealed record ProductionOutput(
    int Id,
    int ProductionOrderId,
    ProductId ProductId,
    decimal Quantity);

public sealed record ComplianceIncident(
    int Id,
    SupplierId SupplierId,
    string Severity,
    DateOnly OccurredOn,
    string Description);
