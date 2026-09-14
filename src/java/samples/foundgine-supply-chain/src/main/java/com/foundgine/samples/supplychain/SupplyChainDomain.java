package com.foundgine.samples.supplychain;

import com.foundgine.providers.aot.*;
import java.math.BigDecimal;
import java.time.*;
import java.util.List;

/** Complete provider-neutral Supply Chain sample domain. */
public final class SupplyChainDomain {
    private SupplyChainDomain() {}
    public record CompanyId(int value) {} public record BusinessUnitId(int value) {}
    public record WarehouseId(int value) {} public record SupplierId(int value) {}
    public record SupplierSiteId(int value) {} public record CertificationId(int value) {}
    public record ProductId(int value) {} public record PurchaseOrderId(int value) {}
    public record PurchaseOrderLineId(int value) {} public record ShipmentId(int value) {}
    public record InventoryLotId(int value) {} public record CustomerOrderId(int value) {}
    public record CustomerOrderLineId(int value) {}

    @FoundgineEntity(name="Company")
    public record Company(int id, String name, @FoundgineSemanticDimension("tenant") String tenantId) {}
    @FoundgineEntity(name="BusinessUnit")
    public record BusinessUnit(int id, @FoundgineSemanticDimension("company") int companyId, String name) {}
    @FoundgineEntity(name="Warehouse")
    public record Warehouse(int id, @FoundgineSemanticDimension("businessUnit") int businessUnitId, String name,
                            @FoundgineSemanticDimension("tenant") String tenantId) {}
    @FoundgineEntity(name="Supplier") @FoundgineAlias(value="Vendor",weight=95) @FoundgineAlias(value="Seller",weight=90)
    public record Supplier(int id,String name,@FoundgineSemanticDimension("country") String country,
                           BigDecimal riskScore,@FoundgineSemanticDimension("tenant") String tenantId) {}
    @FoundgineEntity(name="SupplierSite")
    public record SupplierSite(int id,int supplierId,@FoundgineSemanticDimension("country") String country,String name) {}
    @FoundgineEntity(name="SupplierCertification")
    public record SupplierCertification(int id,int supplierId,String type,LocalDate validFrom,LocalDate validTo) {}
    @FoundgineEntity(name="Product")
    public record Product(int id,String sku,String name,@FoundgineSemanticDimension("category") String category,BigDecimal safetyStock) {}
    @FoundgineEntity(name="ProductComponent")
    public record ProductComponent(int parentProductId,int componentProductId,BigDecimal quantityPerParent,
                                   LocalDate effectiveFrom,LocalDate effectiveTo,String revision,boolean phantom,
                                   BigDecimal yieldLossPercent,BigDecimal scrapFactor) {}
    public enum PurchaseOrderStatus { OPEN, PARTIALLY_RECEIVED, CANCELLED, CLOSED }
    @FoundgineEntity(name="PurchaseOrder") @FoundgineAlias(value="PO",weight=100) @FoundgineAlias(value="purchase",weight=90)
    public record PurchaseOrder(int id,int supplierId,int warehouseId,LocalDate expectedDate,LocalDate receivedDate,PurchaseOrderStatus status) {}
    @FoundgineEntity(name="PurchaseOrderLine")
    public record PurchaseOrderLine(int id,int purchaseOrderId,int productId,int quantity,BigDecimal unitCost) {}
    public enum ShipmentStatus { IN_TRANSIT, DELAYED, PARTIALLY_RECEIVED, RECEIVED }
    @FoundgineEntity(name="Shipment")
    public record Shipment(int id,int purchaseOrderId,int warehouseId,int quantity,LocalDate expectedArrival,ShipmentStatus status) {}
    @FoundgineEntity(name="InventoryLot")
    public record InventoryLot(int id,int warehouseId,int productId,int onHand,int reserved,int quarantined,LocalDate expiryDate) {}
    @FoundgineEntity(name="CustomerOrder")
    public record CustomerOrder(int id,int customerId,LocalDate orderDate,String status) {}
    @FoundgineEntity(name="CustomerOrderLine")
    public record CustomerOrderLine(int id,int customerOrderId,int productId,int quantity,BigDecimal unitPrice) {}
    @FoundgineEntity(name="Customer")
    public record Customer(int id,String name,String tenantId) {}
    @FoundgineEntity(name="ComplianceIncident")
    public record ComplianceIncident(int id,int supplierId,String severity,String description,LocalDate occurredAt) {}
    @FoundgineEntity(name="Carrier")
    public record Carrier(int id,String name,String trackingUrlTemplate) {}
}
