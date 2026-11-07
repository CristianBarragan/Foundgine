package com.foundgine.samples.supplychain.advanced.model;

import com.foundgine.core.semantic.configuration.*;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;

/** Maps every semantic member onto the persistence member that holds it. */
final class EntityMaps {
    private EntityMaps() {}

    static final class CompanyMap extends FoundgineEntityMap<Company, Company> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Company, Company> map) {
            map.property(Company::id).to(Company::id);
            map.property(Company::name).to(Company::name);
            map.property(Company::tenantId).to(Company::tenantId);
        }
    }

    static final class BusinessUnitMap extends FoundgineEntityMap<BusinessUnit, BusinessUnit> {
        @Override
        public void configure(FoundgineEntityMapBuilder<BusinessUnit, BusinessUnit> map) {
            map.property(BusinessUnit::companyId).to(BusinessUnit::companyId);
            map.property(BusinessUnit::id).to(BusinessUnit::id);
            map.property(BusinessUnit::name).to(BusinessUnit::name);
        }
    }

    static final class WarehouseMap extends FoundgineEntityMap<Warehouse, Warehouse> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Warehouse, Warehouse> map) {
            map.property(Warehouse::businessUnitId).to(Warehouse::businessUnitId);
            map.property(Warehouse::id).to(Warehouse::id);
            map.property(Warehouse::name).to(Warehouse::name);
            map.property(Warehouse::tenantId).to(Warehouse::tenantId);
        }
    }

    static final class SupplierMap extends FoundgineEntityMap<Supplier, Supplier> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Supplier, Supplier> map) {
            map.property(Supplier::country).to(Supplier::country);
            map.property(Supplier::id).to(Supplier::id);
            map.property(Supplier::name).to(Supplier::name);
            map.property(Supplier::riskScore).to(Supplier::riskScore);
            map.property(Supplier::tenantId).to(Supplier::tenantId);
        }
    }

    static final class SupplierSiteMap extends FoundgineEntityMap<SupplierSite, SupplierSite> {
        @Override
        public void configure(FoundgineEntityMapBuilder<SupplierSite, SupplierSite> map) {
            map.property(SupplierSite::country).to(SupplierSite::country);
            map.property(SupplierSite::id).to(SupplierSite::id);
            map.property(SupplierSite::name).to(SupplierSite::name);
            map.property(SupplierSite::supplierId).to(SupplierSite::supplierId);
        }
    }

    static final class SupplierCertificationMap extends FoundgineEntityMap<SupplierCertification, SupplierCertification> {
        @Override
        public void configure(FoundgineEntityMapBuilder<SupplierCertification, SupplierCertification> map) {
            map.property(SupplierCertification::id).to(SupplierCertification::id);
            map.property(SupplierCertification::supplierId).to(SupplierCertification::supplierId);
            map.property(SupplierCertification::type).to(SupplierCertification::type);
            map.property(SupplierCertification::validFrom).to(SupplierCertification::validFrom);
            map.property(SupplierCertification::validTo).to(SupplierCertification::validTo);
        }
    }

    static final class ProductMap extends FoundgineEntityMap<Product, Product> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Product, Product> map) {
            map.property(Product::category).to(Product::category);
            map.property(Product::id).to(Product::id);
            map.property(Product::name).to(Product::name);
            map.property(Product::safetyStock).to(Product::safetyStock);
            map.property(Product::sku).to(Product::sku);
            map.property(Product::unitPrice).to(Product::unitPrice);
        }
    }

    static final class ProductComponentMap extends FoundgineEntityMap<ProductComponent, ProductComponent> {
        @Override
        public void configure(FoundgineEntityMapBuilder<ProductComponent, ProductComponent> map) {
            map.property(ProductComponent::componentProductId).to(ProductComponent::componentProductId);
            map.property(ProductComponent::effectiveFrom).to(ProductComponent::effectiveFrom);
            map.property(ProductComponent::effectiveTo).to(ProductComponent::effectiveTo);
            map.property(ProductComponent::parentProductId).to(ProductComponent::parentProductId);
            map.property(ProductComponent::phantom).to(ProductComponent::phantom);
            map.property(ProductComponent::quantityPerParent).to(ProductComponent::quantityPerParent);
            map.property(ProductComponent::revision).to(ProductComponent::revision);
            map.property(ProductComponent::scrapFactor).to(ProductComponent::scrapFactor);
            map.property(ProductComponent::yieldLossPercent).to(ProductComponent::yieldLossPercent);
        }
    }

    static final class PurchaseOrderMap extends FoundgineEntityMap<PurchaseOrder, PurchaseOrder> {
        @Override
        public void configure(FoundgineEntityMapBuilder<PurchaseOrder, PurchaseOrder> map) {
            map.property(PurchaseOrder::expectedArrival).to(PurchaseOrder::expectedArrival);
            map.property(PurchaseOrder::id).to(PurchaseOrder::id);
            map.property(PurchaseOrder::orderedOn).to(PurchaseOrder::orderedOn);
            map.property(PurchaseOrder::status).to(PurchaseOrder::status);
            map.property(PurchaseOrder::supplierId).to(PurchaseOrder::supplierId);
            map.property(PurchaseOrder::warehouseId).to(PurchaseOrder::warehouseId);
        }
    }

    static final class PurchaseOrderLineMap extends FoundgineEntityMap<PurchaseOrderLine, PurchaseOrderLine> {
        @Override
        public void configure(FoundgineEntityMapBuilder<PurchaseOrderLine, PurchaseOrderLine> map) {
            map.property(PurchaseOrderLine::id).to(PurchaseOrderLine::id);
            map.property(PurchaseOrderLine::productId).to(PurchaseOrderLine::productId);
            map.property(PurchaseOrderLine::purchaseOrderId).to(PurchaseOrderLine::purchaseOrderId);
            map.property(PurchaseOrderLine::quantity).to(PurchaseOrderLine::quantity);
            map.property(PurchaseOrderLine::unitPrice).to(PurchaseOrderLine::unitPrice);
        }
    }

    static final class ShipmentMap extends FoundgineEntityMap<Shipment, Shipment> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Shipment, Shipment> map) {
            map.property(Shipment::actualArrival).to(Shipment::actualArrival);
            map.property(Shipment::expectedArrival).to(Shipment::expectedArrival);
            map.property(Shipment::id).to(Shipment::id);
            map.property(Shipment::purchaseOrderId).to(Shipment::purchaseOrderId);
            map.property(Shipment::quantity).to(Shipment::quantity);
            map.property(Shipment::status).to(Shipment::status);
        }
    }

    static final class InventoryLotMap extends FoundgineEntityMap<InventoryLot, InventoryLot> {
        @Override
        public void configure(FoundgineEntityMapBuilder<InventoryLot, InventoryLot> map) {
            map.property(InventoryLot::id).to(InventoryLot::id);
            map.property(InventoryLot::onHand).to(InventoryLot::onHand);
            map.property(InventoryLot::productId).to(InventoryLot::productId);
            map.property(InventoryLot::quarantined).to(InventoryLot::quarantined);
            map.property(InventoryLot::receivedOn).to(InventoryLot::receivedOn);
            map.property(InventoryLot::reserved).to(InventoryLot::reserved);
            map.property(InventoryLot::warehouseId).to(InventoryLot::warehouseId);
        }
    }

    static final class CustomerOrderMap extends FoundgineEntityMap<CustomerOrder, CustomerOrder> {
        @Override
        public void configure(FoundgineEntityMapBuilder<CustomerOrder, CustomerOrder> map) {
            map.property(CustomerOrder::businessUnitId).to(CustomerOrder::businessUnitId);
            map.property(CustomerOrder::id).to(CustomerOrder::id);
            map.property(CustomerOrder::placedOn).to(CustomerOrder::placedOn);
            map.property(CustomerOrder::status).to(CustomerOrder::status);
        }
    }

    static final class CustomerOrderLineMap extends FoundgineEntityMap<CustomerOrderLine, CustomerOrderLine> {
        @Override
        public void configure(FoundgineEntityMapBuilder<CustomerOrderLine, CustomerOrderLine> map) {
            map.property(CustomerOrderLine::customerOrderId).to(CustomerOrderLine::customerOrderId);
            map.property(CustomerOrderLine::id).to(CustomerOrderLine::id);
            map.property(CustomerOrderLine::productId).to(CustomerOrderLine::productId);
            map.property(CustomerOrderLine::quantity).to(CustomerOrderLine::quantity);
        }
    }

    static final class InventoryMovementMap extends FoundgineEntityMap<InventoryMovement, InventoryMovement> {
        @Override
        public void configure(FoundgineEntityMapBuilder<InventoryMovement, InventoryMovement> map) {
            map.property(InventoryMovement::id).to(InventoryMovement::id);
            map.property(InventoryMovement::lotId).to(InventoryMovement::lotId);
            map.property(InventoryMovement::occurredAt).to(InventoryMovement::occurredAt);
            map.property(InventoryMovement::quantity).to(InventoryMovement::quantity);
            map.property(InventoryMovement::reason).to(InventoryMovement::reason);
        }
    }

    static final class ProductionOutputMap extends FoundgineEntityMap<ProductionOutput, ProductionOutput> {
        @Override
        public void configure(FoundgineEntityMapBuilder<ProductionOutput, ProductionOutput> map) {
            map.property(ProductionOutput::id).to(ProductionOutput::id);
            map.property(ProductionOutput::productId).to(ProductionOutput::productId);
            map.property(ProductionOutput::productionOrderId).to(ProductionOutput::productionOrderId);
            map.property(ProductionOutput::quantity).to(ProductionOutput::quantity);
        }
    }

    static final class ComplianceIncidentMap extends FoundgineEntityMap<ComplianceIncident, ComplianceIncident> {
        @Override
        public void configure(FoundgineEntityMapBuilder<ComplianceIncident, ComplianceIncident> map) {
            map.property(ComplianceIncident::description).to(ComplianceIncident::description);
            map.property(ComplianceIncident::id).to(ComplianceIncident::id);
            map.property(ComplianceIncident::occurredOn).to(ComplianceIncident::occurredOn);
            map.property(ComplianceIncident::severity).to(ComplianceIncident::severity);
            map.property(ComplianceIncident::supplierId).to(ComplianceIncident::supplierId);
        }
    }
}
