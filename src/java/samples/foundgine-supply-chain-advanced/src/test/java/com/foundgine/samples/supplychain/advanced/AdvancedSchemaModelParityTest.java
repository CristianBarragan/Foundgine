package com.foundgine.samples.supplychain.advanced;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.migrations.schema.DatabaseSchema;
import com.foundgine.migrations.schema.SchemaBuilder;
import com.foundgine.migrations.diff.SchemaDiffer;
import com.foundgine.migrations.sql.PostgresSqlGenerator;
import com.foundgine.migrations.schema.TableSchema;
import com.foundgine.samples.supplychain.advanced.model.AdvancedPhysicalSchemaModel;
import org.junit.jupiter.api.Test;

/**
 * Locks the fluent physical-schema projection to the complete eleven-table V1 contract:
 * columns, keys, indexes, defaults, identity, foreign keys, CHECK constraints and extensions.
 */
class AdvancedSchemaModelParityTest {
    private final DatabaseSchema schema =
            SchemaBuilder.fromMetadata(new AdvancedPhysicalSchemaModel().build());

    private TableSchema table(String name) {
        return schema.table(name).orElseThrow(() -> new AssertionError("Missing table " + name));
    }

    private void column(TableSchema table, String name, String type) {
        var column = table.column(name).orElseThrow(() -> new AssertionError(
                "Missing column " + table.name() + "." + name));
        assertEquals(type, column.normalizedType(), table.name() + "." + name + " SQL type");
        assertFalse(column.nullable(), table.name() + "." + name + " must be NOT NULL in V1");
    }

    @Test
    void productMatchesPhysicalV1ColumnsAndUniqueSkuIndex() {
        var products = table("products");
        assertEquals(java.util.List.of("product_id"), products.primaryKey());
        column(products, "product_id", "integer");
        column(products, "sku", "varchar(50)");
        column(products, "product_name", "varchar(120)");
        column(products, "category", "varchar(80)");
        column(products, "safety_stock", "numeric(18,2)");
        column(products, "unit_price", "numeric(18,2)");
        var sku = products.index("products_sku_key").orElseThrow();
        assertTrue(sku.unique());
        assertEquals(java.util.List.of("sku"), sku.columns());
    }

    @Test
    void warehouseMatchesPhysicalV1Columns() {
        var warehouses = table("warehouses");
        assertEquals(java.util.List.of("warehouse_id"), warehouses.primaryKey());
        column(warehouses, "warehouse_id", "integer");
        column(warehouses, "warehouse_name", "varchar(120)");
        column(warehouses, "business_unit_id", "integer");
        column(warehouses, "tenant_id", "varchar(80)");
    }

    @Test
    void inventoryLotMatchesPhysicalV1ColumnsAndCompositeIndex() {
        var lots = table("inventory_lots");
        assertEquals(java.util.List.of("lot_id"), lots.primaryKey());
        column(lots, "lot_id", "integer");
        column(lots, "warehouse_id", "integer");
        column(lots, "product_id", "integer");
        column(lots, "on_hand", "numeric(18,2)");
        column(lots, "reserved", "numeric(18,2)");
        column(lots, "quarantined", "numeric(18,2)");
        column(lots, "received_on", "date");
        var index = lots.index("ix_inventory_lots_product_warehouse").orElseThrow();
        assertFalse(index.unique());
        assertEquals(java.util.List.of("product_id", "warehouse_id"), index.columns());
    }

    @Test
    void customerMatchesTransactionalV1Columns() {
        var customers = table("customers");
        assertEquals(java.util.List.of("customer_id"), customers.primaryKey());
        column(customers, "customer_id", "integer");
        column(customers, "customer_name", "varchar(120)");
        column(customers, "tenant_id", "varchar(80)");
    }

    @Test
    void orderAndOrderItemMatchV1ColumnsAndOrderIndex() {
        var orders = table("orders");
        assertEquals(java.util.List.of("order_id"), orders.primaryKey());
        column(orders, "order_id", "integer");
        column(orders, "customer_id", "integer");
        column(orders, "status", "varchar(30)");
        column(orders, "total_amount", "numeric(18,2)");
        column(orders, "placed_on", "date");
        var customerStatus = orders.index("ix_orders_customer_status").orElseThrow();
        assertEquals(java.util.List.of("customer_id", "status"), customerStatus.columns());

        var items = table("order_items");
        assertEquals(java.util.List.of("order_item_id"), items.primaryKey());
        column(items, "order_item_id", "integer");
        column(items, "order_id", "integer");
        column(items, "product_id", "integer");
        column(items, "quantity", "integer");
        column(items, "unit_price", "numeric(18,2)");
    }

    @Test
    void allocationAndIdempotencyTablesMatchV1Columns() {
        var allocations = table("order_allocations");
        assertEquals(java.util.List.of("order_item_id"), allocations.primaryKey());
        column(allocations, "order_item_id", "integer");
        column(allocations, "lot_id", "integer");
        column(allocations, "quantity", "integer");

        var idempotency = table("supply_chain_idempotency");
        assertEquals(java.util.List.of("idempotency_key"), idempotency.primaryKey());
        column(idempotency, "idempotency_key", "varchar(200)");
        column(idempotency, "actor", "varchar(200)");
        column(idempotency, "customer_id", "integer");
        column(idempotency, "order_id", "integer");
        column(idempotency, "request_fingerprint", "char(64)");
        column(idempotency, "created_at", "timestamptz");

        var cancellation = table("supply_chain_cancellation_idempotency");
        assertEquals(java.util.List.of("idempotency_key"), cancellation.primaryKey());
        column(cancellation, "idempotency_key", "varchar(200)");
        column(cancellation, "actor", "varchar(200)");
        column(cancellation, "order_id", "integer");
        column(cancellation, "request_fingerprint", "char(64)");
        column(cancellation, "restored_quantity", "integer");
        column(cancellation, "cancelled_on", "date");
    }

    @Test
    void suppliersMatchesPhysicalV1ColumnsAndUniqueNameIndex() {
        var suppliers = table("suppliers");
        assertEquals(java.util.List.of("supplier_id"), suppliers.primaryKey());
        column(suppliers, "supplier_id", "integer");
        column(suppliers, "supplier_name", "varchar(100)");
        var state = suppliers.column("state").orElseThrow();
        assertEquals("varchar(2)", state.normalizedType());
        assertTrue(state.nullable());
        column(suppliers, "total_order_value", "numeric(14,2)");
        var negotiatedCost = suppliers.column("negotiated_cost").orElseThrow();
        assertEquals("numeric(14,2)", negotiatedCost.normalizedType());
        assertTrue(negotiatedCost.nullable());
        var uniqueName = suppliers.index("suppliers_supplier_name_key").orElseThrow();
        assertTrue(uniqueName.unique());
        assertEquals(java.util.List.of("supplier_name"), uniqueName.columns());
    }

    @Test
    void purchaseOrdersMatchesPhysicalV1ColumnsAndSupplierIndex() {
        var purchaseOrders = table("purchase_orders");
        assertEquals(java.util.List.of("purchase_order_id"), purchaseOrders.primaryKey());
        column(purchaseOrders, "purchase_order_id", "integer");
        column(purchaseOrders, "supplier_id", "integer");
        column(purchaseOrders, "expected_date", "date");
        var receivedDate = purchaseOrders.column("received_date").orElseThrow();
        assertEquals("date", receivedDate.normalizedType());
        assertTrue(receivedDate.nullable());
        column(purchaseOrders, "status", "varchar(20)");
        var supplierIndex = purchaseOrders.index("ix_purchase_orders_supplier").orElseThrow();
        assertFalse(supplierIndex.unique());
        assertEquals(java.util.List.of("supplier_id"), supplierIndex.columns());
    }

    @Test
    void fluentModelPreservesIdentityDefaultsForeignKeysAndChecks() {
        var orders = table("orders");
        var orderId = orders.column("order_id").orElseThrow();
        assertEquals(com.foundgine.migrations.schema.IdentityGeneration.ALWAYS, orderId.identity());
        assertEquals("customers", orders.foreignKey("fk_orders_customer").orElseThrow().referencedTable());
        assertEquals("status IN ('Pending','Cancelled','Processing','Shipped','Delivered')",
                orders.check("ck_orders_status").orElseThrow().expression());

        var items = table("order_items");
        assertEquals(com.foundgine.migrations.schema.ForeignKeySchema.DeleteAction.CASCADE,
                items.foreignKey("fk_order_items_order").orElseThrow().onDelete());
        assertTrue(items.foreignKey("fk_order_items_product").isPresent());
        assertEquals("quantity > 0", items.check("ck_order_items_quantity").orElseThrow().expression());

        var allocations = table("order_allocations");
        assertEquals(com.foundgine.migrations.schema.ForeignKeySchema.DeleteAction.CASCADE,
                allocations.foreignKey("fk_order_allocations_order_item").orElseThrow().onDelete());
        assertTrue(allocations.foreignKey("fk_order_allocations_lot").isPresent());
        assertEquals("quantity > 0", allocations.check("ck_order_allocations_quantity").orElseThrow().expression());

        var products = table("products");
        assertEquals("unit_price >= 0", products.check("ck_products_unit_price").orElseThrow().expression());

        var lots = table("inventory_lots");
        assertEquals("0", lots.column("reserved").orElseThrow().defaultExpression());
        assertEquals("0", lots.column("quarantined").orElseThrow().defaultExpression());
        assertEquals("on_hand >= 0", lots.check("ck_inventory_lots_on_hand").orElseThrow().expression());
        assertEquals("reserved >= 0", lots.check("ck_inventory_lots_reserved").orElseThrow().expression());
        assertEquals("quarantined >= 0", lots.check("ck_inventory_lots_quarantined").orElseThrow().expression());
        assertTrue(lots.foreignKey("fk_inventory_lots_warehouse").isPresent());
        assertTrue(lots.foreignKey("fk_inventory_lots_product").isPresent());

        assertTrue(orders.foreignKey("fk_orders_customer").isPresent());

        var idempotency = table("supply_chain_idempotency");
        assertEquals("CURRENT_TIMESTAMP", idempotency.column("created_at").orElseThrow().defaultExpression());
        assertTrue(idempotency.foreignKey("fk_supply_chain_idempotency_order").isPresent());
        assertTrue(idempotency.foreignKey("fk_supply_chain_idempotency_customer").isPresent());

        var cancellation = table("supply_chain_cancellation_idempotency");
        assertTrue(cancellation.foreignKey("fk_supply_chain_cancellation_order").isPresent());

        var suppliers = table("suppliers");
        assertEquals(com.foundgine.migrations.schema.IdentityGeneration.ALWAYS,
                suppliers.column("supplier_id").orElseThrow().identity());
        assertEquals("0", suppliers.column("total_order_value").orElseThrow().defaultExpression());
        var trigram = suppliers.index("ix_suppliers_name_trgm").orElseThrow();
        assertEquals("gin", trigram.method());
        assertEquals("gin_trgm_ops", trigram.operatorClass());
        assertTrue(schema.extension("pg_trgm").isPresent());
        assertTrue(suppliers.index("suppliers_supplier_name_key").orElseThrow().unique());

        var purchaseOrders = table("purchase_orders");
        assertEquals("'Open'", purchaseOrders.column("status").orElseThrow().defaultExpression());
        assertTrue(purchaseOrders.foreignKey("fk_purchase_orders_supplier").isPresent());
        assertEquals("status IN ('Open','Received','Cancelled')",
                purchaseOrders.check("ck_purchase_orders_status").orElseThrow().expression());
        assertTrue(purchaseOrders.index("ix_purchase_orders_supplier").isPresent());
    }

    @Test
    void generatedSchemaCoversEveryV1ConstraintCategory() {
        assertEquals(11, schema.tables().stream().mapToInt(t -> t.foreignKeys().size()).sum(),
                "V1 declares eleven foreign keys");
        assertEquals(8, schema.tables().stream().mapToInt(t -> t.checks().size()).sum(),
                "V1 declares eight CHECK constraints");
        assertEquals(6, schema.tables().stream().mapToInt(t -> t.indexes().size()).sum(),
                "V1 declares six explicit indexes, including unique indexes");
        assertEquals(5, schema.tables().stream().flatMap(t -> t.columns().stream())
                .filter(c -> c.defaultExpression() != null).count(),
                "V1 declares five column defaults");
        assertEquals(4, schema.tables().stream().flatMap(t -> t.columns().stream())
                .filter(c -> c.identity() != null).count(),
                "V1 declares four identity columns");
    }

    @Test
    void generatedPostgresSqlCarriesTheModeledV1PhysicalFeatures() {
        String sql = PostgresSqlGenerator.up(SchemaDiffer.diff(DatabaseSchema.empty(), schema));
        assertTrue(sql.contains("GENERATED ALWAYS AS IDENTITY"));
        assertTrue(sql.contains("DEFAULT CURRENT_TIMESTAMP"));
        assertTrue(sql.contains("DEFAULT 0"));
        assertTrue(sql.contains("CREATE EXTENSION IF NOT EXISTS \"pg_trgm\""));
        assertTrue(sql.contains("USING gin (\"supplier_name\" gin_trgm_ops)"));
        assertTrue(sql.contains("FOREIGN KEY (\"order_id\") REFERENCES \"orders\""));
        assertTrue(sql.contains("ON DELETE CASCADE"));
        assertTrue(sql.contains("CHECK (quantity > 0)"));
        assertTrue(DatabaseSchema.fromJson(schema.toJson()).equals(schema));
    }

    @Test
    void physicalSchemaProjectionContainsExactlyTheElevenV1Tables() {
        var expected = java.util.Set.of("products", "warehouses", "inventory_lots",
                "customers", "orders", "order_items", "order_allocations",
                "supply_chain_idempotency", "supply_chain_cancellation_idempotency",
                "suppliers", "purchase_orders");
        assertEquals(expected, schema.tables().stream().map(TableSchema::name)
                .collect(java.util.stream.Collectors.toSet()));
    }
}
