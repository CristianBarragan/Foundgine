using Foundgine.SupplyChain.Advanced.Domain;

namespace Foundgine.SupplyChain.Advanced.Data;

public sealed class SupplyChainData
{
    public List<Product> Products { get; } = [];
    public List<ProductComponent> Components { get; } = [];
    public List<Supplier> Suppliers { get; } = [];
    public List<SupplierCertification> Certifications { get; } = [];
    public List<ComplianceIncident> Incidents { get; } = [];
    public List<Warehouse> Warehouses { get; } = [];
    public List<BusinessUnit> BusinessUnits { get; } = [];
    public List<PurchaseOrder> PurchaseOrders { get; } = [];
    public List<PurchaseOrderLine> PurchaseOrderLines { get; } = [];
    public List<Shipment> Shipments { get; } = [];
    public List<InventoryLot> Inventory { get; } = [];
    public List<CustomerOrder> CustomerOrders { get; } = [];
    public List<CustomerOrderLine> CustomerOrderLines { get; } = [];

    public static SupplyChainData Seed()
    {
        var d = new SupplyChainData();
        d.BusinessUnits.AddRange([
            new(new BusinessUnitId(1), new CompanyId(1), "Consumer Electronics"),
            new(new BusinessUnitId(2), new CompanyId(1), "Industrial Systems")
        ]);
        d.Warehouses.AddRange([
            new(new WarehouseId(1), new BusinessUnitId(1), "Auckland DC", "tenant-a"),
            new(new WarehouseId(2), new BusinessUnitId(1), "Hamilton DC", "tenant-a"),
            new(new WarehouseId(3), new BusinessUnitId(2), "Restricted Rotorua DC", "tenant-b")
        ]);
        d.Suppliers.AddRange([
            new(new SupplierId(1), "Kiwi Components", "NZ", 0.22m, "tenant-a"),
            new(new SupplierId(2), "Pacific Semiconductors", "TW", 0.87m, "tenant-a"),
            new(new SupplierId(3), "Global Metals", "US", 0.62m, "tenant-b")
        ]);
        d.Products.AddRange([
            new(new ProductId(1), "MOTOR-X", "Industrial Motor", "Motors", 30),
            new(new ProductId(2), "CTRL-X", "Motor Controller", "Controls", 40),
            new(new ProductId(3), "PCB-X", "Controller PCB", "Electronics", 80),
            new(new ProductId(4), "CAP-X", "High Reliability Capacitor", "Electronics", 120),
            new(new ProductId(5), "RES-X", "Precision Resistor", "Electronics", 100),
            new(new ProductId(6), "PACK-X", "Customer Assembly", "Finished Goods", 25)
        ]);
        d.Components.AddRange([
            new(new ProductId(1), new ProductId(2), 1),
            new(new ProductId(2), new ProductId(3), 2),
            new(new ProductId(3), new ProductId(4), 8),
            new(new ProductId(3), new ProductId(5), 12),
            new(new ProductId(6), new ProductId(1), 2),
            // Deliberate cycle used by the adversarial scenario.
            new(new ProductId(5), new ProductId(2), 1)
        ]);
        d.Certifications.AddRange([
            new(new CertificationId(1), new SupplierId(1), "ISO9001", new DateOnly(2025, 1, 1),
                new DateOnly(2027, 12, 31)),
            new(new CertificationId(2), new SupplierId(2), "ISO9001", new DateOnly(2025, 1, 1),
                new DateOnly(2026, 9, 3)),
            new(new CertificationId(3), new SupplierId(3), "ISO9001", new DateOnly(2024, 1, 1),
                new DateOnly(2026, 8, 1))
        ]);
        d.Incidents.AddRange([
            new(1, new SupplierId(2), "Critical", new DateOnly(2026, 8, 10), "Capacity disruption"),
            new(2, new SupplierId(3), "High", new DateOnly(2026, 7, 4), "Certification lapse")
        ]);
        d.PurchaseOrders.AddRange([
            new(new PurchaseOrderId(100), new SupplierId(2), new WarehouseId(1), PurchaseOrderStatus.Open,
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 29)),
            new(new PurchaseOrderId(101), new SupplierId(1), new WarehouseId(1), PurchaseOrderStatus.PartiallyReceived,
                new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 30)),
            new(new PurchaseOrderId(102), new SupplierId(3), new WarehouseId(3), PurchaseOrderStatus.Cancelled,
                new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 20))
        ]);
        d.PurchaseOrderLines.AddRange([
            new(new PurchaseOrderLineId(1000), new PurchaseOrderId(100), new ProductId(4), 1000, 3.25m),
            new(new PurchaseOrderLineId(1001), new PurchaseOrderId(101), new ProductId(5), 600, 0.42m)
        ]);
        d.Shipments.AddRange([
            new(new ShipmentId(500), new PurchaseOrderId(100), new DateOnly(2026, 8, 29), null, ShipmentStatus.Delayed,
                1000),
            new(new ShipmentId(501), new PurchaseOrderId(101), new DateOnly(2026, 8, 30), null,
                ShipmentStatus.PartiallyReceived, 300)
        ]);
        d.Inventory.AddRange([
            new(new InventoryLotId(900), new WarehouseId(1), new ProductId(4), 70, 20, 10, new DateOnly(2026, 8, 1)),
            new(new InventoryLotId(901), new WarehouseId(1), new ProductId(5), 90, 75, 0, new DateOnly(2026, 8, 2)),
            new(new InventoryLotId(902), new WarehouseId(1), new ProductId(3), 25, 10, 5, new DateOnly(2026, 8, 4)),
            new(new InventoryLotId(903), new WarehouseId(3), new ProductId(4), 5000, 0, 0, new DateOnly(2026, 8, 1))
        ]);
        d.CustomerOrders.AddRange([
            new(new CustomerOrderId(700), new BusinessUnitId(1), new DateOnly(2026, 8, 20), "Open"),
            new(new CustomerOrderId(701), new BusinessUnitId(2), new DateOnly(2026, 8, 21), "Open")
        ]);
        d.CustomerOrderLines.AddRange([
            new(new CustomerOrderLineId(7000), new CustomerOrderId(700), new ProductId(1), 40),
            new(new CustomerOrderLineId(7001), new CustomerOrderId(700), new ProductId(6), 20),
            new(new CustomerOrderLineId(7002), new CustomerOrderId(701), new ProductId(2), 100)
        ]);
        return d;
    }
}