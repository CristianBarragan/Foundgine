package com.foundgine.samples.supplychain.model;

import com.foundgine.core.semantic.configuration.FoundgineModel;
import com.foundgine.core.semantic.configuration.FoundgineModelRegistrar;

/**
 * Physical-schema projection of the starter Supply Chain model.
 *
 * The application model also registers semantic configurations and entity maps. Migration
 * generation intentionally compiles only persistence configurations, so only the three owned
 * relational tables become database objects. Table and column declarations remain centralized in
 * {@link PersistenceConfigurations}; this class does not duplicate schema definitions.
 */
public final class SimplePhysicalSchemaModel extends FoundgineModel {
    @Override
    public void configure(FoundgineModelRegistrar model) {
        model.configure(PersistenceConfigurations.SupplierStorage.class);
        model.configure(PersistenceConfigurations.WarehouseStorage.class);
        model.configure(PersistenceConfigurations.ProductStorage.class);
    }
}
