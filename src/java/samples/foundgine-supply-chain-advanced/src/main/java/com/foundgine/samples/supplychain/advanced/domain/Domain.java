package com.foundgine.samples.supplychain.advanced.domain;

import java.math.BigDecimal;
import java.time.*;

/**
 * Annotation-free Supply Chain domain. Semantic names, aliases, dimensions, storage and
 * relationships are declared in {@code com.foundgine.samples.supplychain.advanced.model}.
 */
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

    public record Company(int id, String name, String tenantId) {}

    public record BusinessUnit(int id, int companyId, String name) {}

    public record Warehouse(int id, int businessUnitId, String name, String tenantId) {}

    public record Supplier(
            int id, String name, String country, BigDecimal riskScore, String tenantId) {}

    public record SupplierSite(int id, int supplierId, String country, String name) {}

    public record SupplierCertification(
            int id, int supplierId, String type, LocalDate validFrom, LocalDate validTo) {}

    public record Product(
            int id,
            String sku,
            String name,
            String category,
            BigDecimal safetyStock,
            BigDecimal unitPrice) {}

    public record ProductComponent(
            int parentProductId,
            int componentProductId,
            BigDecimal quantityPerParent,
            LocalDate effectiveFrom,
            LocalDate effectiveTo,
            String revision,
            boolean phantom,
            BigDecimal yieldLossPercent,
            BigDecimal scrapFactor) {}

    public enum PurchaseOrderStatus {
        OPEN,
        PARTIALLY_RECEIVED,
        CANCELLED,
        CLOSED
    }

    public record PurchaseOrder(
            int id,
            int supplierId,
            int warehouseId,
            PurchaseOrderStatus status,
            LocalDate orderedOn,
            LocalDate expectedArrival) {}

    public record PurchaseOrderLine(
            int id,
            int purchaseOrderId,
            int productId,
            BigDecimal quantity,
            BigDecimal unitPrice) {}

    public enum ShipmentStatus {
        PLANNED,
        IN_TRANSIT,
        DELAYED,
        PARTIALLY_RECEIVED,
        RECEIVED,
        CANCELLED
    }

    public record Shipment(
            int id,
            int purchaseOrderId,
            LocalDate expectedArrival,
            LocalDate actualArrival,
            ShipmentStatus status,
            BigDecimal quantity) {}

    public record InventoryLot(
            int id,
            int warehouseId,
            int productId,
            BigDecimal onHand,
            BigDecimal reserved,
            BigDecimal quarantined,
            LocalDate receivedOn) {}

    public record Customer(int id, String name, String tenantId) {}

    public record Order(
            int id, int customerId, String status, BigDecimal totalAmount, LocalDate placedOn) {}

    public record OrderItem(
            int id, int orderId, int productId, int quantity, BigDecimal unitPrice) {}

    public record OrderAllocation(int orderItemId, int lotId, int quantity) {}

    public record CancellationIdempotencyRecord(
            String key,
            String actor,
            int orderId,
            String requestFingerprint,
            BigDecimal restoredQuantity,
            LocalDate cancelledOn) {}

    public record IdempotencyRecord(
            String key, String actor, int customerId, int orderId, String requestFingerprint) {}

    public record CustomerOrder(int id, int businessUnitId, LocalDate placedOn, String status) {}

    public record CustomerOrderLine(
            int id, int customerOrderId, int productId, BigDecimal quantity) {}

    public record InventoryMovement(
            int id, int lotId, BigDecimal quantity, String reason, OffsetDateTime occurredAt) {}

    public record Allocation(int orderLineId, int lotId, BigDecimal quantity) {}

    public record ProductionOrder(int id, int productId, BigDecimal quantity, String status) {}

    public record ProductionMaterial(
            int id, int productionOrderId, int productId, BigDecimal quantity) {}

    public record ProductionOutput(
            int id, int productionOrderId, int productId, BigDecimal quantity) {}

    public record ComplianceIncident(
            int id, int supplierId, String severity, LocalDate occurredOn, String description) {}
}
