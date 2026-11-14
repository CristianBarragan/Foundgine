package com.foundgine.samples.supplychain.model;

import com.foundgine.core.semantic.configuration.*;
import com.foundgine.samples.supplychain.domain.Domain.*;

/** Maps every semantic member onto the persistence member that holds it (the domain types double as both). */
final class EntityMaps {
    private EntityMaps() {}

    static final class SupplierMap extends FoundgineEntityMap<Supplier, Supplier> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Supplier, Supplier> map) {
            map.property(Supplier::id).to(Supplier::id);
            map.property(Supplier::name).to(Supplier::name);
            map.property(Supplier::country).to(Supplier::country);
            map.property(Supplier::riskScore).to(Supplier::riskScore);
            map.property(Supplier::tenantId).to(Supplier::tenantId);
        }
    }

    static final class WarehouseMap extends FoundgineEntityMap<Warehouse, Warehouse> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Warehouse, Warehouse> map) {
            map.property(Warehouse::id).to(Warehouse::id);
            map.property(Warehouse::businessUnitId).to(Warehouse::businessUnitId);
            map.property(Warehouse::name).to(Warehouse::name);
            map.property(Warehouse::tenantId).to(Warehouse::tenantId);
        }
    }

    static final class ProductMap extends FoundgineEntityMap<Product, Product> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Product, Product> map) {
            map.property(Product::id).to(Product::id);
            map.property(Product::sku).to(Product::sku);
            map.property(Product::name).to(Product::name);
            map.property(Product::category).to(Product::category);
            map.property(Product::safetyStock).to(Product::safetyStock);
        }
    }
}
