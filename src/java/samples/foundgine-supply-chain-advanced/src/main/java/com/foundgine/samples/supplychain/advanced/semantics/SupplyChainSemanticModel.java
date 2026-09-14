package com.foundgine.samples.supplychain.advanced.semantics;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.metadata.*;
import com.foundgine.samples.supplychain.advanced.generated.GeneratedFoundgineMetadata;

/** Application semantic model for the Advanced Supply Chain sample. */
public final class SupplyChainSemanticModel {
    public static final IMetadataCatalog Metadata = GeneratedFoundgineMetadata.build();

    public static final EntityId PRODUCT = entity("Product");
    public static final EntityId COMPONENT = entity("ProductComponent");
    public static final EntityId SUPPLIER = entity("Supplier");
    public static final EntityId SHIPMENT = entity("Shipment");
    public static final EntityId INVENTORY_LOT = entity("InventoryLot");
    public static final EntityId WAREHOUSE = entity("Warehouse");
    public static final EntityId BUSINESS_UNIT = entity("BusinessUnit");
    public static final EntityId PURCHASE_ORDER = entity("PurchaseOrder");
    public static final EntityId PURCHASE_ORDER_LINE = entity("PurchaseOrderLine");
    public static final EntityId CERTIFICATION = entity("SupplierCertification");
    public static final EntityId COMPLIANCE_INCIDENT = entity("ComplianceIncident");

    public static final SemanticModel MODEL = build();

    public static SemanticModel build() {
        return SemanticModelDiscovery.fromMetadata(Metadata)
                .overlay(ManualSupplyChainSemanticModel.MODEL)
                .traversal(
                        "Product", "shipments", "purchaseOrderLines", "purchaseOrder", "shipments")
                .traversal(
                        "Product",
                        "supplierIncidents",
                        "purchaseOrderLines",
                        "purchaseOrder",
                        "supplier",
                        "incidents")
                .build()
                .freeze();
    }

    public static EntityId entity(String name) {
        for (var e : Metadata.entities()) {
            if (e.name().equalsIgnoreCase(name)) return e.entityId();
        }
        throw new IllegalStateException("Unknown metadata entity '" + name + "'.");
    }

    public static FieldId field(String entity, String field) {
        return MODEL.get(entity(entity)).fields().stream()
                .filter(f -> f.name().equalsIgnoreCase(field))
                .findFirst()
                .orElseThrow()
                .id();
    }

    public static RelationshipId relationship(String entity, String relationship) {
        return MODEL.get(entity(entity)).relationships().stream()
                .filter(r -> r.name().equalsIgnoreCase(relationship))
                .findFirst()
                .orElseThrow()
                .id();
    }

    private SupplyChainSemanticModel() {}
}
