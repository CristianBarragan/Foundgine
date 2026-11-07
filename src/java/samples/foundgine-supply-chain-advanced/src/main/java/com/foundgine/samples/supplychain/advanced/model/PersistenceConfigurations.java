package com.foundgine.samples.supplychain.advanced.model;

import com.foundgine.core.semantic.configuration.*;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;

/** Storage side: tables, keys and indexes for each persistence type. */
final class PersistenceConfigurations {
    private PersistenceConfigurations() {}

    static final class CompanyStorage extends FoundginePersistenceConfiguration<Company> {
        @Override
        public void configure(FoundginePersistenceBuilder<Company> model) {
            model.toTable("Company");
            model.hasKey(Company::id);
        }
    }

    static final class BusinessUnitStorage extends FoundginePersistenceConfiguration<BusinessUnit> {
        @Override
        public void configure(FoundginePersistenceBuilder<BusinessUnit> model) {
            model.toTable("BusinessUnit");
            model.hasKey(BusinessUnit::id);
            model.hasIndex(BusinessUnit::companyId);
        }
    }

    static final class WarehouseStorage extends FoundginePersistenceConfiguration<Warehouse> {
        @Override
        public void configure(FoundginePersistenceBuilder<Warehouse> model) {
            model.toTable("Warehouse");
            model.hasKey(Warehouse::id);
        }
    }

    static final class SupplierStorage extends FoundginePersistenceConfiguration<Supplier> {
        @Override
        public void configure(FoundginePersistenceBuilder<Supplier> model) {
            model.toTable("Supplier");
            model.hasKey(Supplier::id);
        }
    }

    static final class SupplierSiteStorage extends FoundginePersistenceConfiguration<SupplierSite> {
        @Override
        public void configure(FoundginePersistenceBuilder<SupplierSite> model) {
            model.toTable("SupplierSite");
            model.hasKey(SupplierSite::id);
        }
    }

    static final class SupplierCertificationStorage extends FoundginePersistenceConfiguration<SupplierCertification> {
        @Override
        public void configure(FoundginePersistenceBuilder<SupplierCertification> model) {
            model.toTable("SupplierCertification");
            model.hasKey(SupplierCertification::id);
        }
    }

    static final class ProductStorage extends FoundginePersistenceConfiguration<Product> {
        @Override
        public void configure(FoundginePersistenceBuilder<Product> model) {
            model.toTable("Product");
            model.hasKey(Product::id);
        }
    }

    static final class ProductComponentStorage extends FoundginePersistenceConfiguration<ProductComponent> {
        @Override
        public void configure(FoundginePersistenceBuilder<ProductComponent> model) {
            model.toTable("ProductComponent");
            model.hasKey(ProductComponent::parentProductId);
        }
    }

    static final class PurchaseOrderStorage extends FoundginePersistenceConfiguration<PurchaseOrder> {
        @Override
        public void configure(FoundginePersistenceBuilder<PurchaseOrder> model) {
            model.toTable("PurchaseOrder");
            model.hasKey(PurchaseOrder::id);
        }
    }

    static final class PurchaseOrderLineStorage extends FoundginePersistenceConfiguration<PurchaseOrderLine> {
        @Override
        public void configure(FoundginePersistenceBuilder<PurchaseOrderLine> model) {
            model.toTable("PurchaseOrderLine");
            model.hasKey(PurchaseOrderLine::id);
        }
    }

    static final class ShipmentStorage extends FoundginePersistenceConfiguration<Shipment> {
        @Override
        public void configure(FoundginePersistenceBuilder<Shipment> model) {
            model.toTable("Shipment");
            model.hasKey(Shipment::id);
        }
    }

    static final class InventoryLotStorage extends FoundginePersistenceConfiguration<InventoryLot> {
        @Override
        public void configure(FoundginePersistenceBuilder<InventoryLot> model) {
            model.toTable("InventoryLot");
            model.hasKey(InventoryLot::id);
        }
    }

    static final class CustomerOrderStorage extends FoundginePersistenceConfiguration<CustomerOrder> {
        @Override
        public void configure(FoundginePersistenceBuilder<CustomerOrder> model) {
            model.toTable("CustomerOrder");
            model.hasKey(CustomerOrder::id);
        }
    }

    static final class CustomerOrderLineStorage extends FoundginePersistenceConfiguration<CustomerOrderLine> {
        @Override
        public void configure(FoundginePersistenceBuilder<CustomerOrderLine> model) {
            model.toTable("CustomerOrderLine");
            model.hasKey(CustomerOrderLine::id);
        }
    }

    static final class InventoryMovementStorage extends FoundginePersistenceConfiguration<InventoryMovement> {
        @Override
        public void configure(FoundginePersistenceBuilder<InventoryMovement> model) {
            model.toTable("InventoryMovement");
            model.hasKey(InventoryMovement::id);
        }
    }

    static final class ProductionOutputStorage extends FoundginePersistenceConfiguration<ProductionOutput> {
        @Override
        public void configure(FoundginePersistenceBuilder<ProductionOutput> model) {
            model.toTable("ProductionOutput");
            model.hasKey(ProductionOutput::id);
        }
    }

    static final class ComplianceIncidentStorage extends FoundginePersistenceConfiguration<ComplianceIncident> {
        @Override
        public void configure(FoundginePersistenceBuilder<ComplianceIncident> model) {
            model.toTable("ComplianceIncident");
            model.hasKey(ComplianceIncident::id);
        }
    }
}
