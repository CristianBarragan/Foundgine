package com.foundgine.samples.supplychain.model;

import com.foundgine.core.semantic.configuration.*;
import com.foundgine.samples.supplychain.domain.Domain.*;

/**
 * Storage side: tables, columns, keys and indexes. This is the source of the database schema: the migration
 * tooling (foundgine-migrations) diffs it against the last snapshot and writes the Flyway migration, the way
 * EF derives migrations from its model. After changing anything here run {@code migrations add <Name>}.
 */
final class PersistenceConfigurations {
    private PersistenceConfigurations() {}

    static final class SupplierStorage extends FoundginePersistenceConfiguration<Supplier> {
        @Override
        public void configure(FoundginePersistenceBuilder<Supplier> model) {
            model.toTable("suppliers");
            model.hasKey(Supplier::id);
            model.property(Supplier::id).hasColumnName("supplier_id");
            model.property(Supplier::name).hasColumnName("supplier_name").hasMaxLength(100).isRequired();
            model.property(Supplier::country).hasColumnName("country").hasMaxLength(2).isRequired();
            model.property(Supplier::riskScore).hasColumnName("risk_score").hasPrecision(5, 2).isRequired();
            model.property(Supplier::tenantId).hasColumnName("tenant_id").hasMaxLength(50).isRequired();
            model.hasIndex(Supplier::tenantId);
        }
    }

    static final class WarehouseStorage extends FoundginePersistenceConfiguration<Warehouse> {
        @Override
        public void configure(FoundginePersistenceBuilder<Warehouse> model) {
            model.toTable("warehouses");
            model.hasKey(Warehouse::id);
            model.property(Warehouse::id).hasColumnName("warehouse_id");
            model.property(Warehouse::businessUnitId).hasColumnName("business_unit_id");
            model.property(Warehouse::name).hasColumnName("warehouse_name").hasMaxLength(100).isRequired();
            model.property(Warehouse::tenantId).hasColumnName("tenant_id").hasMaxLength(50).isRequired();
            model.hasIndex(Warehouse::tenantId);
        }
    }

    static final class ProductStorage extends FoundginePersistenceConfiguration<Product> {
        @Override
        public void configure(FoundginePersistenceBuilder<Product> model) {
            model.toTable("products");
            model.hasKey(Product::id);
            model.property(Product::id).hasColumnName("product_id");
            model.property(Product::sku).hasColumnName("sku").hasMaxLength(50).isRequired();
            model.property(Product::name).hasColumnName("product_name").hasMaxLength(100).isRequired();
            model.property(Product::category).hasColumnName("category").hasMaxLength(50).isRequired();
            model.property(Product::safetyStock).hasColumnName("safety_stock").hasPrecision(10, 2).isRequired();
            model.hasUniqueIndex(Product::sku);
        }
    }
}
