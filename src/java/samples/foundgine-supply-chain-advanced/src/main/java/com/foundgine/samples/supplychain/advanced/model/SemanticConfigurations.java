package com.foundgine.samples.supplychain.advanced.model;

import com.foundgine.core.semantic.configuration.*;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;

/** Semantic side: entity names, aliases, dimensions, events and relationships. Says nothing about storage. */
final class SemanticConfigurations {
    private SemanticConfigurations() {}

    static final class CompanySemantics extends FoundgineModelConfiguration<Company> {
        @Override
        public void configure(FoundgineModelBuilder<Company> model) {
            model.toEntity("Company");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasDimension(Company::tenantId).as("tenant");
        }
    }

    static final class BusinessUnitSemantics extends FoundgineModelConfiguration<BusinessUnit> {
        @Override
        public void configure(FoundgineModelBuilder<BusinessUnit> model) {
            model.toEntity("BusinessUnit");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasDimension(BusinessUnit::companyId).as("company");
        }
    }

    static final class WarehouseSemantics extends FoundgineModelConfiguration<Warehouse> {
        @Override
        public void configure(FoundgineModelBuilder<Warehouse> model) {
            model.toEntity("Warehouse");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasDimension(Warehouse::businessUnitId).as("businessUnit");
            model.hasDimension(Warehouse::tenantId).as("tenant");
            model.hasMany("inventory", InventoryLot.class);
            model.hasOne("businessUnit", BusinessUnit.class);
        }
    }

    static final class SupplierSemantics extends FoundgineModelConfiguration<Supplier> {
        @Override
        public void configure(FoundgineModelBuilder<Supplier> model) {
            model.toEntity("Supplier");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasAlias("Vendor", 95);
            model.hasAlias("Seller", 90);
            model.hasDimension(Supplier::country).as("country");
            model.hasDimension(Supplier::tenantId).as("tenant");
            model.property(Supplier::country).hasAlias("State", 85);
            model.hasMany("certifications", SupplierCertification.class);
            model.hasMany("incidents", ComplianceIncident.class);
            model.hasMany("purchaseOrders", PurchaseOrder.class);
        }
    }

    static final class SupplierSiteSemantics extends FoundgineModelConfiguration<SupplierSite> {
        @Override
        public void configure(FoundgineModelBuilder<SupplierSite> model) {
            model.toEntity("SupplierSite");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasDimension(SupplierSite::country).as("country");
        }
    }

    static final class SupplierCertificationSemantics extends FoundgineModelConfiguration<SupplierCertification> {
        @Override
        public void configure(FoundgineModelBuilder<SupplierCertification> model) {
            model.toEntity("SupplierCertification");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
        }
    }

    static final class ProductSemantics extends FoundgineModelConfiguration<Product> {
        @Override
        public void configure(FoundgineModelBuilder<Product> model) {
            model.toEntity("Product");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasDimension(Product::category).as("category");
            model.hasMany("components", ProductComponent.class);
            model.hasMany("purchaseOrderLines", PurchaseOrderLine.class);
            model.hasMany("usedInComponents", ProductComponent.class);
        }
    }

    static final class ProductComponentSemantics extends FoundgineModelConfiguration<ProductComponent> {
        @Override
        public void configure(FoundgineModelBuilder<ProductComponent> model) {
            model.toEntity("ProductComponent");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasOne("componentProduct", Product.class)
                    .withMany("usedInComponents");
        }
    }

    static final class PurchaseOrderSemantics extends FoundgineModelConfiguration<PurchaseOrder> {
        @Override
        public void configure(FoundgineModelBuilder<PurchaseOrder> model) {
            model.toEntity("PurchaseOrder");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasAlias("PO", 100);
            model.hasAlias("POs", 95);
            model.hasAlias("Buy", 90);
            model.hasAlias("Buys", 85);
            model.property(PurchaseOrder::expectedArrival).hasAlias("DueDate", 90);
            model.property(PurchaseOrder::supplierId).hasAlias("vendor", 85);
            model.hasMany("lines", PurchaseOrderLine.class);
            model.hasOne("supplier", Supplier.class)
                    .hasAlias("vendor", 85);
            model.hasMany("shipments", Shipment.class);
        }
    }

    static final class PurchaseOrderLineSemantics extends FoundgineModelConfiguration<PurchaseOrderLine> {
        @Override
        public void configure(FoundgineModelBuilder<PurchaseOrderLine> model) {
            model.toEntity("PurchaseOrderLine");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasOne("purchaseOrder", PurchaseOrder.class);
        }
    }

    static final class ShipmentSemantics extends FoundgineModelConfiguration<Shipment> {
        @Override
        public void configure(FoundgineModelBuilder<Shipment> model) {
            model.toEntity("Shipment");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
        }
    }

    static final class InventoryLotSemantics extends FoundgineModelConfiguration<InventoryLot> {
        @Override
        public void configure(FoundgineModelBuilder<InventoryLot> model) {
            model.toEntity("InventoryLot");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasOne("warehouse", Warehouse.class);
        }
    }

    static final class CustomerOrderSemantics extends FoundgineModelConfiguration<CustomerOrder> {
        @Override
        public void configure(FoundgineModelBuilder<CustomerOrder> model) {
            model.toEntity("CustomerOrder");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasMany("lines", CustomerOrderLine.class);
        }
    }

    static final class CustomerOrderLineSemantics extends FoundgineModelConfiguration<CustomerOrderLine> {
        @Override
        public void configure(FoundgineModelBuilder<CustomerOrderLine> model) {
            model.toEntity("CustomerOrderLine");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
        }
    }

    static final class InventoryMovementSemantics extends FoundgineModelConfiguration<InventoryMovement> {
        @Override
        public void configure(FoundgineModelBuilder<InventoryMovement> model) {
            model.toEntity("InventoryMovement");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.isEvent(InventoryMovement::occurredAt);
        }
    }

    static final class ProductionOutputSemantics extends FoundgineModelConfiguration<ProductionOutput> {
        @Override
        public void configure(FoundgineModelBuilder<ProductionOutput> model) {
            model.toEntity("ProductionOutput");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.isEvent();
        }
    }

    static final class ComplianceIncidentSemantics extends FoundgineModelConfiguration<ComplianceIncident> {
        @Override
        public void configure(FoundgineModelBuilder<ComplianceIncident> model) {
            model.toEntity("ComplianceIncident");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.isEvent(ComplianceIncident::occurredOn);
        }
    }
}
