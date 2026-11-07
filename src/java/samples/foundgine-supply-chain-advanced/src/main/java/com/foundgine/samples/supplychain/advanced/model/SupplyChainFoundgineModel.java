package com.foundgine.samples.supplychain.advanced.model;

import com.foundgine.core.semantic.configuration.FoundgineModel;
import com.foundgine.core.semantic.configuration.FoundgineModelRegistrar;

/**
 * Single registration point for the Advanced Supply Chain model. {@link #build()} produces the
 * metadata registry the semantic model, planner and providers consume.
 */
public final class SupplyChainFoundgineModel extends FoundgineModel {
    @Override
    public void configure(FoundgineModelRegistrar model) {
        model.configure(SemanticConfigurations.CompanySemantics.class);
        model.configure(SemanticConfigurations.BusinessUnitSemantics.class);
        model.configure(SemanticConfigurations.WarehouseSemantics.class);
        model.configure(SemanticConfigurations.SupplierSemantics.class);
        model.configure(SemanticConfigurations.SupplierSiteSemantics.class);
        model.configure(SemanticConfigurations.SupplierCertificationSemantics.class);
        model.configure(SemanticConfigurations.ProductSemantics.class);
        model.configure(SemanticConfigurations.ProductComponentSemantics.class);
        model.configure(SemanticConfigurations.PurchaseOrderSemantics.class);
        model.configure(SemanticConfigurations.PurchaseOrderLineSemantics.class);
        model.configure(SemanticConfigurations.ShipmentSemantics.class);
        model.configure(SemanticConfigurations.InventoryLotSemantics.class);
        model.configure(SemanticConfigurations.CustomerOrderSemantics.class);
        model.configure(SemanticConfigurations.CustomerOrderLineSemantics.class);
        model.configure(SemanticConfigurations.InventoryMovementSemantics.class);
        model.configure(SemanticConfigurations.ProductionOutputSemantics.class);
        model.configure(SemanticConfigurations.ComplianceIncidentSemantics.class);

        model.configure(PersistenceConfigurations.CompanyStorage.class);
        model.configure(PersistenceConfigurations.BusinessUnitStorage.class);
        model.configure(PersistenceConfigurations.WarehouseStorage.class);
        model.configure(PersistenceConfigurations.SupplierStorage.class);
        model.configure(PersistenceConfigurations.SupplierSiteStorage.class);
        model.configure(PersistenceConfigurations.SupplierCertificationStorage.class);
        model.configure(PersistenceConfigurations.ProductStorage.class);
        model.configure(PersistenceConfigurations.ProductComponentStorage.class);
        model.configure(PersistenceConfigurations.PurchaseOrderStorage.class);
        model.configure(PersistenceConfigurations.PurchaseOrderLineStorage.class);
        model.configure(PersistenceConfigurations.ShipmentStorage.class);
        model.configure(PersistenceConfigurations.InventoryLotStorage.class);
        model.configure(PersistenceConfigurations.CustomerOrderStorage.class);
        model.configure(PersistenceConfigurations.CustomerOrderLineStorage.class);
        model.configure(PersistenceConfigurations.InventoryMovementStorage.class);
        model.configure(PersistenceConfigurations.ProductionOutputStorage.class);
        model.configure(PersistenceConfigurations.ComplianceIncidentStorage.class);

        model.configure(EntityMaps.CompanyMap.class);
        model.configure(EntityMaps.BusinessUnitMap.class);
        model.configure(EntityMaps.WarehouseMap.class);
        model.configure(EntityMaps.SupplierMap.class);
        model.configure(EntityMaps.SupplierSiteMap.class);
        model.configure(EntityMaps.SupplierCertificationMap.class);
        model.configure(EntityMaps.ProductMap.class);
        model.configure(EntityMaps.ProductComponentMap.class);
        model.configure(EntityMaps.PurchaseOrderMap.class);
        model.configure(EntityMaps.PurchaseOrderLineMap.class);
        model.configure(EntityMaps.ShipmentMap.class);
        model.configure(EntityMaps.InventoryLotMap.class);
        model.configure(EntityMaps.CustomerOrderMap.class);
        model.configure(EntityMaps.CustomerOrderLineMap.class);
        model.configure(EntityMaps.InventoryMovementMap.class);
        model.configure(EntityMaps.ProductionOutputMap.class);
        model.configure(EntityMaps.ComplianceIncidentMap.class);

        model.configure(SupplyChainConnections.class);
        model.configure(SupplyChainModelConnections.class);
    }
}
