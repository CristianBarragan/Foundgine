package com.foundgine.samples.supplychain.advanced.semantics;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.samples.supplychain.advanced.domain.Domain;

/** Application semantic model for the Advanced Supply Chain sample. */
public final class SupplyChainSemanticModel {
    public static final EntityId PRODUCT = EntityId.create("Product");
    public static final EntityId COMPONENT = EntityId.create("ProductComponent");
    public static final EntityId SUPPLIER = EntityId.create("Supplier");
    public static final EntityId SHIPMENT = EntityId.create("Shipment");
    public static final EntityId INVENTORY_LOT = EntityId.create("InventoryLot");
    public static final EntityId WAREHOUSE = EntityId.create("Warehouse");
    public static final EntityId BUSINESS_UNIT = EntityId.create("BusinessUnit");
    public static final EntityId CUSTOMER_ORDER = EntityId.create("CustomerOrder");
    public static final EntityId CUSTOMER_ORDER_LINE = EntityId.create("CustomerOrderLine");
    public static final EntityId CUSTOMER = EntityId.create("Customer");
    public static final EntityId ORDER = EntityId.create("Order");
    public static final EntityId ORDER_ITEM = EntityId.create("OrderItem");
    public static final EntityId PURCHASE_ORDER = EntityId.create("PurchaseOrder");
    public static final EntityId PURCHASE_ORDER_LINE = EntityId.create("PurchaseOrderLine");
    public static final EntityId CERTIFICATION = EntityId.create("SupplierCertification");
    public static final EntityId COMPLIANCE_INCIDENT = EntityId.create("ComplianceIncident");
    public static final SemanticModel MODEL = build();

    public static SemanticModel build() {
        var b = new SemanticModelBuilder();
        entity(b, "Supplier", Domain.Supplier.class, "id", e -> {
            e.relationship("certifications", CERTIFICATION, RelationshipCardinality.MANY);
            e.relationship("incidents", COMPLIANCE_INCIDENT, RelationshipCardinality.MANY);
        });
        entity(b, "SupplierCertification", Domain.SupplierCertification.class, "id", e -> {});
        entity(b, "ComplianceIncident", Domain.ComplianceIncident.class, "id", e -> {});
        entity(b, "Warehouse", Domain.Warehouse.class, "id", e ->
            e.relationship("inventory", INVENTORY_LOT, RelationshipCardinality.MANY));
        entity(b, "BusinessUnit", Domain.BusinessUnit.class, "id", e -> {});
        entity(b, "PurchaseOrder", Domain.PurchaseOrder.class, "id", e -> {
            e.relationship("lines", PURCHASE_ORDER_LINE, RelationshipCardinality.MANY);
            e.relationship("shipments", SHIPMENT, RelationshipCardinality.MANY);
            e.relationship("supplier", SUPPLIER, RelationshipCardinality.ONE);
        });
        entity(b, "PurchaseOrderLine", Domain.PurchaseOrderLine.class, "id", e -> {
            e.relationship("purchaseOrder", PURCHASE_ORDER, RelationshipCardinality.ONE);
            e.relationship("product", PRODUCT, RelationshipCardinality.ONE);
        });
        entity(b, "Shipment", Domain.Shipment.class, "id", e ->
            e.relationship("purchaseOrder", PURCHASE_ORDER, RelationshipCardinality.ONE));
        entity(b, "InventoryLot", Domain.InventoryLot.class, "id", e -> {
            e.relationship("warehouse", WAREHOUSE, RelationshipCardinality.ONE);
            e.relationship("product", PRODUCT, RelationshipCardinality.ONE);
        });
        entity(b, "CustomerOrder", Domain.CustomerOrder.class, "id", e ->
            e.relationship("lines", CUSTOMER_ORDER_LINE, RelationshipCardinality.MANY));
        entity(b, "CustomerOrderLine", Domain.CustomerOrderLine.class, "id", e -> {
            e.relationship("customerOrder", CUSTOMER_ORDER, RelationshipCardinality.ONE);
            e.relationship("product", PRODUCT, RelationshipCardinality.ONE);
        });
        entity(b, "Product", Domain.Product.class, "id", e ->
            e.relationship("components", COMPONENT, RelationshipCardinality.MANY));
        entity(b, "ProductComponent", Domain.ProductComponent.class, "parentProductId", e ->
            e.relationship("componentProduct", PRODUCT, RelationshipCardinality.ONE));

        b.entity(EntityId.create("PlaceOrderCommand"), "PlaceOrderCommand", e -> {
            e.identity(FieldId.create("PlaceOrderCommand", "OrderId"), "OrderId");
            commandField(e, "PlaceOrderCommand", "Actor", String.class); commandField(e, "PlaceOrderCommand", "CustomerId", int.class);
            commandField(e, "PlaceOrderCommand", "ProductId", int.class); commandField(e, "PlaceOrderCommand", "Quantity", int.class);
            commandField(e, "PlaceOrderCommand", "IdempotencyKey", String.class); commandField(e, "PlaceOrderCommand", "OrderId", int.class);
        });
        b.entity(EntityId.create("CancelOrderCommand"), "CancelOrderCommand", e -> {
            e.identity(FieldId.create("CancelOrderCommand", "OrderId"), "OrderId");
            commandField(e, "CancelOrderCommand", "Actor", String.class); commandField(e, "CancelOrderCommand", "OrderId", int.class);
            commandField(e, "CancelOrderCommand", "IdempotencyKey", String.class);
        });
        return b.overlay(ManualSupplyChainSemanticModel.MODEL).build().freeze();
    }

    @FunctionalInterface private interface Configure { void apply(SemanticEntityBuilder builder); }
    private static void entity(SemanticModelBuilder b, String name, Class<?> type, String identityField, Configure configure) {
        b.entity(EntityId.create(name), name, e -> {
            String identity = pascal(identityField);
            e.identity(FieldId.create(name, identity), identity);
            if (type.isRecord()) {
                for (var c : type.getRecordComponents()) {
                    String field = pascal(c.getName());
                    e.field(FieldId.create(name, field), field, c.getType(), null, SemanticFieldCapabilities.DEFAULT);
                }
            }
            configure.apply(e);
        });
    }
    private static void commandField(SemanticEntityBuilder e, String entity, String name, Class<?> type) {
        e.field(FieldId.create(entity, name), name, type, null,
                (byte)(SemanticFieldCapabilities.DEFAULT | SemanticFieldCapabilities.WRITABLE));
    }
    private static String pascal(String value) { return Character.toUpperCase(value.charAt(0)) + value.substring(1); }
    public static EntityId entity(String name) { return EntityId.create(name); }
    public static FieldId field(String entity, String field) { return FieldId.create(entity, field); }
    public static RelationshipId relationship(String entity, String relationship) { return RelationshipId.create(entity, relationship); }
    private SupplyChainSemanticModel() {}
}
