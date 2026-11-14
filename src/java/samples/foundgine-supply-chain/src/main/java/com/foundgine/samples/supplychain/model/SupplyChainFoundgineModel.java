package com.foundgine.samples.supplychain.model;

import com.foundgine.core.semantic.configuration.FoundgineModel;
import com.foundgine.core.semantic.configuration.FoundgineModelRegistrar;

/** Single registration point for the starter's Foundgine model; {@link #build()} yields the metadata registry. */
public final class SupplyChainFoundgineModel extends FoundgineModel {
    @Override
    public void configure(FoundgineModelRegistrar model) {
        model.configure(SemanticConfigurations.SupplierSemantics.class);
        model.configure(SemanticConfigurations.WarehouseSemantics.class);
        model.configure(SemanticConfigurations.ProductSemantics.class);

        model.configure(PersistenceConfigurations.SupplierStorage.class);
        model.configure(PersistenceConfigurations.WarehouseStorage.class);
        model.configure(PersistenceConfigurations.ProductStorage.class);

        model.configure(EntityMaps.SupplierMap.class);
        model.configure(EntityMaps.WarehouseMap.class);
        model.configure(EntityMaps.ProductMap.class);
    }
}
