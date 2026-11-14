package com.foundgine.samples.supplychain.advanced.model;

import com.foundgine.core.semantic.configuration.FoundgineModel;
import com.foundgine.core.semantic.configuration.FoundgineModelRegistrar;

/**
 * Physical-schema projection of the Advanced model. It deliberately registers only the eleven
 * PostgreSQL tables owned by the Advanced V1 database. Semantic/planning persistence placeholders
 * remain in {@link SupplyChainFoundgineModel}, but are not mistaken for physical V1 tables.
 *
 * All declarations are shared with the application model through PersistenceConfigurations; this
 * class is a projection, not a second set of table/column definitions.
 */
public final class AdvancedPhysicalSchemaModel extends FoundgineModel {
    @Override
    public void configure(FoundgineModelRegistrar model) {
        model.configure(PersistenceConfigurations.WarehouseStorage.class);
        model.configure(PersistenceConfigurations.ProductStorage.class);
        model.configure(PersistenceConfigurations.InventoryLotStorage.class);
        model.configure(PersistenceConfigurations.CustomerRowStorage.class);
        model.configure(PersistenceConfigurations.OrderRowStorage.class);
        model.configure(PersistenceConfigurations.OrderItemRowStorage.class);
        model.configure(PersistenceConfigurations.OrderAllocationRowStorage.class);
        model.configure(PersistenceConfigurations.IdempotencyRowStorage.class);
        model.configure(PersistenceConfigurations.CancellationIdempotencyRowStorage.class);
        model.configure(PersistenceConfigurations.SupplierRowStorage.class);
        model.configure(PersistenceConfigurations.PurchaseOrderRowStorage.class);
    }
}
