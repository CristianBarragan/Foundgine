package com.foundgine.samples.supplychain.advanced.domain;

import java.math.BigDecimal;
import java.time.*;

/** Provider-neutral domain model ported from Foundgine.SupplyChain.Advanced. */
public final class Domain {
    private Domain() {}

    public record CompanyId(int value) {}
    public record BusinessUnitId(int value) {}
    public record WarehouseId(int value) {}
    public record SupplierId(int value) {}
    public record SupplierSiteId(int value) {}
    public record CertificationId(int value) {}
    public record ProductId(int value) {}
    public record PurchaseOrderId(int value) {}
    public record PurchaseOrderLineId(int value) {}
    public record ShipmentId(int value) {}
    public record InventoryLotId(int value) {}
    public record CustomerOrderId(int value) {}
    public record CustomerOrderLineId(int value) {}

    public record Company(CompanyId id, String name, String tenantId) {}
    public record BusinessUnit(BusinessUnitId id, CompanyId companyId, String name) {}
    public record Warehouse(WarehouseId id, BusinessUnitId businessUnitId, String name, String tenantId) {}
    public record Supplier(SupplierId id, String name, String country, BigDecimal riskScore, String tenantId) {}
    public record SupplierSite(SupplierSiteId id, SupplierId supplierId, String country, String name) {}
    public record SupplierCertification(CertificationId id, SupplierId supplierId, String type, LocalDate validFrom, LocalDate validTo) {}
    public record ComplianceIncident(int id, SupplierId supplierId, String severity, LocalDate occurredOn, String description) {}
    public record Product(ProductId id, String sku, String name, String category, BigDecimal safetyStock) {}
    public record ProductComponent(ProductId parentProductId, ProductId componentProductId, BigDecimal quantityPerParent,
                                   LocalDate effectiveFrom, LocalDate effectiveTo, String revision,
                                   boolean phantom, BigDecimal yieldLossPercent, BigDecimal scrapFactor) {}

    public enum PurchaseOrderStatus { OPEN, PARTIALLY_RECEIVED, CANCELLED, CLOSED }
    public record PurchaseOrder(PurchaseOrderId id, SupplierId supplierId, WarehouseId warehouseId,
                                PurchaseOrderStatus status, LocalDate orderedOn, LocalDate expectedArrival) {}
    public record PurchaseOrderLine(PurchaseOrderLineId id, PurchaseOrderId purchaseOrderId,
                                     ProductId productId, BigDecimal quantity, BigDecimal unitPrice) {}

    public enum ShipmentStatus { PLANNED, IN_TRANSIT, DELAYED, PARTIALLY_RECEIVED, RECEIVED, CANCELLED }
    public record Shipment(ShipmentId id, PurchaseOrderId purchaseOrderId, LocalDate expectedArrival,
                           LocalDate actualArrival, ShipmentStatus status, BigDecimal quantity) {}
    public record InventoryLot(InventoryLotId id, WarehouseId warehouseId, ProductId productId,
                               BigDecimal onHand, BigDecimal reserved, BigDecimal quarantined, LocalDate receivedOn) {}
    public record CustomerOrder(CustomerOrderId id, BusinessUnitId businessUnitId, LocalDate placedOn, String status) {}
    public record CustomerOrderLine(CustomerOrderLineId id, CustomerOrderId customerOrderId, ProductId productId,
                                    BigDecimal quantity) {}

    public record InventoryMovement(int id, InventoryLotId lotId, BigDecimal quantity, String reason,
                                    OffsetDateTime occurredAt) {}
    public record Allocation(CustomerOrderLineId orderLineId, InventoryLotId lotId, BigDecimal quantity) {}
    public record ProductionOrder(int id, ProductId productId, BigDecimal quantity, String status) {}
    public record ProductionMaterial(int id, int productionOrderId, ProductId productId, BigDecimal quantity) {}
    public record ProductionOutput(int id, int productionOrderId, ProductId productId, BigDecimal quantity) {}
}
