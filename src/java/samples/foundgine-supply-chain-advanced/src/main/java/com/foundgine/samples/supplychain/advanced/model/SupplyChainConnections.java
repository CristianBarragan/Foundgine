package com.foundgine.samples.supplychain.advanced.model;

import com.foundgine.core.semantic.configuration.*;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;

/** Physical connections: which keys join each pair of related entities. */
final class SupplyChainConnections extends FoundgineConnectionConfiguration {
    @Override
    public void configure(FoundgineConnectionBuilder connections) {
        connections.hasRelationship(Warehouse.class, InventoryLot.class)
                .using(Warehouse.class, InventoryLot.class)
                .hasForeignKey(InventoryLot::warehouseId)
                .references(Warehouse::id);

        connections.hasRelationship(Supplier.class, SupplierCertification.class)
                .using(Supplier.class, SupplierCertification.class)
                .hasForeignKey(SupplierCertification::supplierId)
                .references(Supplier::id);

        connections.hasRelationship(Supplier.class, ComplianceIncident.class)
                .using(Supplier.class, ComplianceIncident.class)
                .hasForeignKey(ComplianceIncident::supplierId)
                .references(Supplier::id);

        connections.hasRelationship(Supplier.class, PurchaseOrder.class)
                .using(Supplier.class, PurchaseOrder.class)
                .hasForeignKey(PurchaseOrder::supplierId)
                .references(Supplier::id);

        connections.hasRelationship(Product.class, ProductComponent.class, "components")
                .using(Product.class, ProductComponent.class)
                .hasForeignKey(ProductComponent::parentProductId)
                .references(Product::id);

        connections.hasRelationship(Product.class, PurchaseOrderLine.class)
                .using(Product.class, PurchaseOrderLine.class)
                .hasForeignKey(PurchaseOrderLine::productId)
                .references(Product::id);

        connections.hasRelationship(Product.class, ProductComponent.class, "usedInComponents")
                .using(Product.class, ProductComponent.class)
                .hasForeignKey(ProductComponent::componentProductId)
                .references(Product::id);

        connections.hasRelationship(PurchaseOrder.class, PurchaseOrderLine.class)
                .using(PurchaseOrder.class, PurchaseOrderLine.class)
                .hasForeignKey(PurchaseOrderLine::purchaseOrderId)
                .references(PurchaseOrder::id);

        connections.hasRelationship(PurchaseOrder.class, Shipment.class)
                .using(PurchaseOrder.class, Shipment.class)
                .hasForeignKey(Shipment::purchaseOrderId)
                .references(PurchaseOrder::id);

        connections.hasRelationship(CustomerOrder.class, CustomerOrderLine.class)
                .using(CustomerOrder.class, CustomerOrderLine.class)
                .hasForeignKey(CustomerOrderLine::customerOrderId)
                .references(CustomerOrder::id);

        connections.hasRelationship(Warehouse.class, BusinessUnit.class)
                .using(Warehouse.class, BusinessUnit.class)
                .hasForeignKeyOnSource(Warehouse::businessUnitId)
                .references(BusinessUnit::id);
    }
}
