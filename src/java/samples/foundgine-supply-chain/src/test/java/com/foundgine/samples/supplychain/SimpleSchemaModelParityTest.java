package com.foundgine.samples.supplychain;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.migrations.schema.SchemaBuilder;
import com.foundgine.samples.supplychain.model.SimplePhysicalSchemaModel;
import java.util.List;
import org.junit.jupiter.api.Test;

/** Locks the starter's physical projection to its three-table fluent persistence model. */
class SimpleSchemaModelParityTest {
    @Test
    void physicalProjectionContainsExactlyTheThreeStarterTables() {
        var schema = SchemaBuilder.fromMetadata(new SimplePhysicalSchemaModel().build());
        assertEquals(List.of("products", "suppliers", "warehouses"),
                schema.tables().stream().map(t -> t.name()).toList());
    }

    @Test
    void persistenceDeclarationsProduceExpectedKeysColumnsAndIndexes() {
        var schema = SchemaBuilder.fromMetadata(new SimplePhysicalSchemaModel().build());

        var products = schema.table("products").orElseThrow();
        assertEquals(List.of("product_id"), products.primaryKey());
        assertEquals("varchar(50)", products.column("sku").orElseThrow().type());
        assertTrue(products.index("ux_products_sku").orElseThrow().unique());

        var suppliers = schema.table("suppliers").orElseThrow();
        assertEquals(List.of("supplier_id"), suppliers.primaryKey());
        assertEquals("numeric(5,2)", suppliers.column("risk_score").orElseThrow().type());
        assertTrue(suppliers.index("ix_suppliers_tenant_id").isPresent());

        var warehouses = schema.table("warehouses").orElseThrow();
        assertEquals(List.of("warehouse_id"), warehouses.primaryKey());
        assertEquals("integer", warehouses.column("business_unit_id").orElseThrow().type());
        assertTrue(warehouses.index("ix_warehouses_tenant_id").isPresent());
    }
}
