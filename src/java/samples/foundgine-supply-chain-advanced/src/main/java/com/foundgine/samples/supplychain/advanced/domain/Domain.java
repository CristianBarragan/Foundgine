package com.foundgine.samples.supplychain.advanced.domain;

import com.foundgine.providers.aot.*;
import java.math.BigDecimal;
import java.time.*;
import java.util.List;

public final class Domain {
  private Domain() {}
  public record CompanyId(int value) {} public record BusinessUnitId(int value) {}
  public record WarehouseId(int value) {} public record SupplierId(int value) {}
  public record SupplierSiteId(int value) {} public record CertificationId(int value) {}
  public record ProductId(int value) {} public record PurchaseOrderId(int value) {}
  public record PurchaseOrderLineId(int value) {} public record ShipmentId(int value) {}
  public record InventoryLotId(int value) {} public record CustomerOrderId(int value) {}
  public record CustomerOrderLineId(int value) {}

  @FoundgineEntity(name="Company") public record Company(int id,String name,@FoundgineSemanticDimension("tenant") String tenantId) {}
  @FoundgineEntity(name="BusinessUnit") public record BusinessUnit(int id,@FoundgineSemanticDimension("company") @FoundgineRelationship(target=Company.class,foreignKey="id",principalKey="companyId",name="company") int companyId,String name) {}
  @FoundgineEntity(name="Warehouse") public record Warehouse(@FoundgineRelationship(target=InventoryLot.class,foreignKey="warehouseId",principalKey="id",name="inventory") int id,@FoundgineSemanticDimension("businessUnit") @FoundgineRelationship(target=BusinessUnit.class,foreignKey="id",principalKey="businessUnitId",name="businessUnit") int businessUnitId,String name,@FoundgineSemanticDimension("tenant") String tenantId) {}
  @FoundgineEntity(name="Supplier") @FoundgineAlias(value="Vendor",weight=95) @FoundgineAlias(value="Seller",weight=90)
  public record Supplier(@FoundgineRelationship(target=SupplierCertification.class,foreignKey="supplierId",principalKey="id",name="certifications") int id,@FoundgineRelationship(target=ComplianceIncident.class,foreignKey="supplierId",principalKey="id",name="incidents") String name,@FoundgineSemanticDimension("country") String country,@FoundgineRelationship(target=PurchaseOrder.class,foreignKey="supplierId",principalKey="id",name="purchaseOrders") BigDecimal riskScore,@FoundgineSemanticDimension("tenant") String tenantId) {}
  public record SupplierSite(int id,int supplierId,@FoundgineSemanticDimension("country") String country,String name) {}
  @FoundgineEntity(name="SupplierCertification") public record SupplierCertification(int id,int supplierId,String type,LocalDate validFrom,LocalDate validTo) {}
  @FoundgineEntity(name="Product") public record Product(@FoundgineRelationship(target=ProductComponent.class,foreignKey="parentProductId",principalKey="id",name="components") int id,String sku,String name,@FoundgineSemanticDimension("category") String category,BigDecimal safetyStock,BigDecimal unitPrice) {}
  @FoundgineEntity(name="ProductComponent") public record ProductComponent(int parentProductId,@FoundgineRelationship(target=Product.class,foreignKey="id",principalKey="componentProductId",name="componentProduct") int componentProductId,BigDecimal quantityPerParent,LocalDate effectiveFrom,LocalDate effectiveTo,String revision,boolean phantom,BigDecimal yieldLossPercent,BigDecimal scrapFactor) {}
  public enum PurchaseOrderStatus { OPEN, PARTIALLY_RECEIVED, CANCELLED, CLOSED }
  @FoundgineEntity(name="PurchaseOrder") @FoundgineAlias(value="PO",weight=100) @FoundgineAlias(value="POs",weight=95) @FoundgineAlias(value="Buy",weight=90) @FoundgineAlias(value="Buys",weight=85)
  public record PurchaseOrder(@FoundgineRelationship(target=PurchaseOrderLine.class,foreignKey="purchaseOrderId",principalKey="id",name="lines") int id,@FoundgineRelationship(target=Supplier.class,foreignKey="id",principalKey="supplierId",name="supplier") int supplierId,@FoundgineRelationship(target=Shipment.class,foreignKey="purchaseOrderId",principalKey="id",name="shipments") int warehouseId,PurchaseOrderStatus status,LocalDate orderedOn,@FoundgineAlias(value="DueDate",weight=90) LocalDate expectedArrival) {}
  @FoundgineEntity(name="PurchaseOrderLine") public record PurchaseOrderLine(int id,@FoundgineRelationship(target=PurchaseOrder.class,foreignKey="id",principalKey="purchaseOrderId",name="purchaseOrder") int purchaseOrderId,int productId,BigDecimal quantity,BigDecimal unitPrice) {}
  public enum ShipmentStatus { PLANNED, IN_TRANSIT, DELAYED, PARTIALLY_RECEIVED, RECEIVED, CANCELLED }
  @FoundgineEntity(name="Shipment") public record Shipment(int id,int purchaseOrderId,LocalDate expectedArrival,LocalDate actualArrival,ShipmentStatus status,BigDecimal quantity) {}
  @FoundgineEntity(name="InventoryLot") public record InventoryLot(int id,@FoundgineRelationship(target=Warehouse.class,foreignKey="id",principalKey="warehouseId",name="warehouse") int warehouseId,int productId,BigDecimal onHand,BigDecimal reserved,BigDecimal quarantined,LocalDate receivedOn) {}
  public record Customer(int id,String name,String tenantId) {}
  @FoundgineEntity(name="Order") public record Order(@FoundgineRelationship(target=OrderItem.class,foreignKey="orderId",principalKey="id",name="items") int id,int customerId,String status,BigDecimal totalAmount,LocalDate placedOn) {}
  @FoundgineEntity(name="OrderItem") public record OrderItem(int id,int orderId,@FoundgineRelationship(target=Product.class,foreignKey="id",principalKey="productId",name="product") int productId,int quantity,BigDecimal unitPrice) {}
  public record OrderAllocation(int orderItemId,int lotId,int quantity) {}
  public record CancellationIdempotencyRecord(String key,String actor,int orderId,String requestFingerprint,BigDecimal restoredQuantity,LocalDate cancelledOn) {}
  public record IdempotencyRecord(String key,String actor,int customerId,int orderId,String requestFingerprint) {}
  public record CustomerOrder(int id,int businessUnitId,LocalDate placedOn,String status) {}
  @FoundgineEntity(name="CustomerOrderLine") public record CustomerOrderLine(int id,int customerOrderId,int productId,BigDecimal quantity) {}
  @FoundgineEntity(name="InventoryMovement") @FoundgineEvent(occurredAtField="OccurredAt") public record InventoryMovement(int id,int lotId,BigDecimal quantity,String reason,OffsetDateTime occurredAt) {}
  public record Allocation(int orderLineId,int lotId,BigDecimal quantity) {}
  public record ProductionOrder(int id,int productId,BigDecimal quantity,String status) {}
  public record ProductionMaterial(int id,int productionOrderId,int productId,BigDecimal quantity) {}
  @FoundgineEntity(name="ProductionOutput") @FoundgineEvent public record ProductionOutput(int id,int productionOrderId,int productId,BigDecimal quantity) {}
  @FoundgineEntity(name="ComplianceIncident") @FoundgineEvent(occurredAtField="OccurredOn") public record ComplianceIncident(int id,int supplierId,String severity,LocalDate occurredOn,String description) {}
}
