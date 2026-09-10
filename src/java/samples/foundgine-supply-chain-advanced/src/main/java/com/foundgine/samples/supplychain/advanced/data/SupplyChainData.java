package com.foundgine.samples.supplychain.advanced.data;

import com.foundgine.samples.supplychain.advanced.domain.Domain.*;
import java.math.BigDecimal;
import java.time.LocalDate;
import java.util.*;

/** Deterministic fixture matching the advanced SupplyChain C# sample. */
public final class SupplyChainData {
    public final List<Product> products = new ArrayList<>();
    public final List<ProductComponent> components = new ArrayList<>();
    public final List<Supplier> suppliers = new ArrayList<>();
    public final List<SupplierCertification> certifications = new ArrayList<>();
    public final List<ComplianceIncident> incidents = new ArrayList<>();
    public final List<Warehouse> warehouses = new ArrayList<>();
    public final List<BusinessUnit> businessUnits = new ArrayList<>();
    public final List<PurchaseOrder> purchaseOrders = new ArrayList<>();
    public final List<PurchaseOrderLine> purchaseOrderLines = new ArrayList<>();
    public final List<Shipment> shipments = new ArrayList<>();
    public final List<InventoryLot> inventory = new ArrayList<>();
    public final List<CustomerOrder> customerOrders = new ArrayList<>();
    public final List<CustomerOrderLine> customerOrderLines = new ArrayList<>();

    public static SupplyChainData seed() {
        var d = new SupplyChainData();
        d.businessUnits.addAll(List.of(
            new BusinessUnit(new BusinessUnitId(1), new CompanyId(1), "Consumer Electronics"),
            new BusinessUnit(new BusinessUnitId(2), new CompanyId(1), "Industrial Systems")));
        d.warehouses.addAll(List.of(
            new Warehouse(new WarehouseId(1), new BusinessUnitId(1), "Auckland DC", "tenant-a"),
            new Warehouse(new WarehouseId(2), new BusinessUnitId(1), "Hamilton DC", "tenant-a"),
            new Warehouse(new WarehouseId(3), new BusinessUnitId(2), "Restricted Rotorua DC", "tenant-b")));
        d.suppliers.addAll(List.of(
            new Supplier(new SupplierId(1), "Kiwi Components", "NZ", bd("0.22"), "tenant-a"),
            new Supplier(new SupplierId(2), "Pacific Semiconductors", "TW", bd("0.87"), "tenant-a"),
            new Supplier(new SupplierId(3), "Global Metals", "US", bd("0.62"), "tenant-b")));
        d.products.addAll(List.of(
            new Product(new ProductId(1), "MOTOR-X", "Industrial Motor", "Motors", bd("30")),
            new Product(new ProductId(2), "CTRL-X", "Motor Controller", "Controls", bd("40")),
            new Product(new ProductId(3), "PCB-X", "Controller PCB", "Electronics", bd("80")),
            new Product(new ProductId(4), "CAP-X", "High Reliability Capacitor", "Electronics", bd("120")),
            new Product(new ProductId(5), "RES-X", "Precision Resistor", "Electronics", bd("100")),
            new Product(new ProductId(6), "PACK-X", "Customer Assembly", "Finished Goods", bd("25"))));
        d.components.addAll(List.of(
            component(1,2,"1"), component(2,3,"2"), component(3,4,"8"), component(3,5,"12"),
            component(6,1,"2"), component(5,2,"1"))); // deliberate cycle
        d.certifications.addAll(List.of(
            new SupplierCertification(new CertificationId(1), new SupplierId(1), "ISO9001", LocalDate.of(2025,1,1), LocalDate.of(2027,12,31)),
            new SupplierCertification(new CertificationId(2), new SupplierId(2), "ISO9001", LocalDate.of(2025,1,1), LocalDate.of(2026,9,3)),
            new SupplierCertification(new CertificationId(3), new SupplierId(3), "ISO9001", LocalDate.of(2024,1,1), LocalDate.of(2026,8,1))));
        d.incidents.addAll(List.of(
            new ComplianceIncident(1,new SupplierId(2),"Critical",LocalDate.of(2026,8,10),"Capacity disruption"),
            new ComplianceIncident(2,new SupplierId(3),"High",LocalDate.of(2026,7,4),"Certification lapse")));
        d.purchaseOrders.addAll(List.of(
            new PurchaseOrder(new PurchaseOrderId(100),new SupplierId(2),new WarehouseId(1),PurchaseOrderStatus.OPEN,LocalDate.of(2026,8,1),LocalDate.of(2026,8,29)),
            new PurchaseOrder(new PurchaseOrderId(101),new SupplierId(1),new WarehouseId(1),PurchaseOrderStatus.PARTIALLY_RECEIVED,LocalDate.of(2026,8,3),LocalDate.of(2026,8,30)),
            new PurchaseOrder(new PurchaseOrderId(102),new SupplierId(3),new WarehouseId(3),PurchaseOrderStatus.CANCELLED,LocalDate.of(2026,7,1),LocalDate.of(2026,8,20))));
        d.purchaseOrderLines.addAll(List.of(
            new PurchaseOrderLine(new PurchaseOrderLineId(1000),new PurchaseOrderId(100),new ProductId(4),bd("1000"),bd("3.25")),
            new PurchaseOrderLine(new PurchaseOrderLineId(1001),new PurchaseOrderId(101),new ProductId(5),bd("600"),bd("0.42"))));
        d.shipments.addAll(List.of(
            new Shipment(new ShipmentId(500),new PurchaseOrderId(100),LocalDate.of(2026,8,29),null,ShipmentStatus.DELAYED,bd("1000")),
            new Shipment(new ShipmentId(501),new PurchaseOrderId(101),LocalDate.of(2026,8,30),null,ShipmentStatus.PARTIALLY_RECEIVED,bd("300"))));
        d.inventory.addAll(List.of(
            new InventoryLot(new InventoryLotId(900),new WarehouseId(1),new ProductId(4),bd("70"),bd("20"),bd("10"),LocalDate.of(2026,8,1)),
            new InventoryLot(new InventoryLotId(901),new WarehouseId(1),new ProductId(5),bd("90"),bd("75"),bd("0"),LocalDate.of(2026,8,2)),
            new InventoryLot(new InventoryLotId(902),new WarehouseId(1),new ProductId(3),bd("25"),bd("10"),bd("5"),LocalDate.of(2026,8,4)),
            new InventoryLot(new InventoryLotId(903),new WarehouseId(3),new ProductId(4),bd("5000"),bd("0"),bd("0"),LocalDate.of(2026,8,1))));
        d.customerOrders.addAll(List.of(
            new CustomerOrder(new CustomerOrderId(700),new BusinessUnitId(1),LocalDate.of(2026,8,20),"Open"),
            new CustomerOrder(new CustomerOrderId(701),new BusinessUnitId(2),LocalDate.of(2026,8,21),"Open")));
        d.customerOrderLines.addAll(List.of(
            new CustomerOrderLine(new CustomerOrderLineId(7000),new CustomerOrderId(700),new ProductId(1),bd("40")),
            new CustomerOrderLine(new CustomerOrderLineId(7001),new CustomerOrderId(700),new ProductId(6),bd("20")),
            new CustomerOrderLine(new CustomerOrderLineId(7002),new CustomerOrderId(701),new ProductId(2),bd("100"))));
        return d;
    }

    private static ProductComponent component(int parent,int child,String qty) {
        return new ProductComponent(new ProductId(parent),new ProductId(child),bd(qty),null,null,"1",false,bd("0"),bd("0"));
    }
    private static BigDecimal bd(String v) { return new BigDecimal(v); }
}
