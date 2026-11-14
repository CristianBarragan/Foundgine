package com.foundgine.migrations;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.migrations.diff.Operation;
import com.foundgine.migrations.diff.SchemaDiffer;
import com.foundgine.migrations.schema.*;
import com.foundgine.migrations.sql.PostgresSqlGenerator;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Set;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class MigrationsTest {
    private static TableSchema users(boolean withEmail, String nameType) {
        var columns = new java.util.ArrayList<>(List.of(
                new ColumnSchema("user_id", "integer", false), new ColumnSchema("name", nameType, false)));
        if (withEmail) columns.add(new ColumnSchema("email", "varchar(120)", true));
        return new TableSchema("users", columns, List.of("user_id"), List.of(new IndexSchema("ix_users_name", List.of("name"), false)));
    }

    @Test
    void identicalSchemasHaveNoOperations() {
        var schema = new DatabaseSchema(List.of(users(true, "varchar(50)")));
        assertTrue(SchemaDiffer.diff(schema, schema).isEmpty());
    }

    @Test
    void typesCompareIgnoringCaseAndSpaces() {
        var a = new DatabaseSchema(List.of(users(false, "NUMERIC(5, 2)")));
        var b = new DatabaseSchema(List.of(users(false, "numeric(5,2)")));
        assertTrue(SchemaDiffer.diff(a, b).isEmpty());
    }

    @Test
    void detectsAddedColumnAndChangedType() {
        var from = new DatabaseSchema(List.of(users(false, "varchar(50)")));
        var to = new DatabaseSchema(List.of(users(true, "varchar(80)")));
        var ops = SchemaDiffer.diff(from, to);
        assertEquals(2, ops.size());
        assertTrue(ops.stream().anyMatch(o -> o instanceof Operation.AlterColumnType t && t.to().equals("varchar(80)")));
        assertTrue(ops.stream().anyMatch(o -> o instanceof Operation.AddColumn a && a.column().name().equals("email")));
    }

    @Test
    void droppedTableIsLastAndWarns() {
        var from = new DatabaseSchema(List.of(users(false, "text")));
        var ops = SchemaDiffer.diff(from, DatabaseSchema.empty());
        assertEquals(1, ops.size());
        assertNotNull(ops.get(0).warning());
        assertTrue(PostgresSqlGenerator.up(ops).contains("DROP TABLE \"users\";"));
    }

    @Test
    void primaryKeyChangeIsRefused() {
        var from = new DatabaseSchema(List.of(users(false, "text")));
        var changed = new TableSchema("users", from.tables().get(0).columns(), List.of("name"), List.of());
        assertThrows(UnsupportedOperationException.class, () -> SchemaDiffer.diff(from, new DatabaseSchema(List.of(changed))));
    }

    @Test
    void createTableSqlQuotesAndQualifies() {
        var table = new TableSchema("logistics.carriers",
                List.of(new ColumnSchema("carrier_id", "integer", false)), List.of("carrier_id"), List.of());
        String sql = PostgresSqlGenerator.up(List.of(new Operation.CreateTable(table)));
        assertTrue(sql.contains("CREATE SCHEMA IF NOT EXISTS \"logistics\";"));
        assertTrue(sql.contains("CREATE TABLE \"logistics\".\"carriers\""));
        assertTrue(sql.contains("CONSTRAINT \"pk_carriers\" PRIMARY KEY (\"carrier_id\")"));
    }

    @Test
    void downReversesInReverseOrder() {
        var from = new DatabaseSchema(List.of(users(false, "text")));
        var to = new DatabaseSchema(List.of(users(true, "text")));
        String down = PostgresSqlGenerator.down(SchemaDiffer.diff(from, to));
        assertTrue(down.contains("DROP COLUMN \"email\""));
    }

    @Test
    void snapshotRoundTripsThroughJson() {
        var schema = new DatabaseSchema(List.of(users(true, "varchar(50)")));
        assertEquals(schema, DatabaseSchema.fromJson(schema.toJson()));
    }

    @Test
    void addListRemoveWorkflow(@TempDir Path project) throws Exception {
        var clock = Clock.fixed(Instant.parse("2026-11-15T10:00:00Z"), ZoneOffset.UTC);
        var service = new MigrationsService(MigrationsWorkspace.forProject(project), clock);
        var v1 = new DatabaseSchema(List.of(users(false, "varchar(50)")));
        var v2 = new DatabaseSchema(List.of(users(true, "varchar(50)")));

        assertThrows(IllegalArgumentException.class, () -> service.add("bad name", v1));
        var first = service.add("InitialCreate", v1);
        assertEquals("20261115100000", first.migration().version());
        assertTrue(Files.exists(first.migration().path()));
        assertFalse(service.hasPendingModelChanges(v1));
        assertThrows(IllegalStateException.class, () -> service.add("Nothing", v1));

        // Same clock second: version must still advance.
        var second = service.add("AddEmail", v2);
        assertEquals("20261115100001", second.migration().version());
        assertEquals(2, service.list().size());
        assertTrue(service.script("20261115100000").contains("ADD COLUMN \"email\""));
        assertFalse(service.script("20261115100000").contains("CREATE TABLE"));

        assertThrows(IllegalStateException.class, () -> service.remove(Set.of("20261115100001"), false));
        service.remove(Set.of(), false);
        assertEquals(1, service.list().size());
        assertTrue(service.hasPendingModelChanges(v2));
        assertFalse(service.hasPendingModelChanges(v1));

        service.remove(Set.of(), false);
        assertTrue(service.list().isEmpty());
        assertEquals(DatabaseSchema.empty(), service.snapshot());
    }


    @Test
    void createsIdentityDefaultsChecksAndPostgresIndexes() {
        var table = new TableSchema("catalog.products", List.of(
                new ColumnSchema("product_id", "bigint", false, null, IdentityGeneration.BY_DEFAULT),
                new ColumnSchema("name", "text", false),
                new ColumnSchema("created_at", "timestamptz", false, "CURRENT_TIMESTAMP", null)),
                List.of("product_id"), List.of(new IndexSchema("ix_products_name_trgm", List.of("name"), false, "gin", "gin_trgm_ops")),
                List.of(), List.of(new CheckConstraintSchema("ck_products_name", "length(name) > 0")));
        String sql = PostgresSqlGenerator.up(List.of(new Operation.CreateTable(table)));
        assertTrue(sql.contains("GENERATED BY DEFAULT AS IDENTITY"));
        assertTrue(sql.contains("DEFAULT CURRENT_TIMESTAMP"));
        assertTrue(sql.contains("CONSTRAINT \"ck_products_name\" CHECK (length(name) > 0)"));
        assertTrue(sql.contains("USING gin (\"name\" gin_trgm_ops)"));
    }

    @Test
    void createsForeignKeysWithDeleteActionsAndExtensions() {
        var fk = new ForeignKeySchema("fk_orders_customer", List.of("customer_id"), "customers", List.of("customer_id"), ForeignKeySchema.DeleteAction.CASCADE, ForeignKeySchema.DeleteAction.NO_ACTION);
        var ops = List.<Operation>of(new Operation.CreateExtension(new PostgresExtensionSchema("pg_trgm")), new Operation.AddForeignKey("orders", fk));
        String sql = PostgresSqlGenerator.up(ops);
        assertTrue(sql.contains("CREATE EXTENSION IF NOT EXISTS \"pg_trgm\""));
        assertTrue(sql.contains("FOREIGN KEY (\"customer_id\") REFERENCES \"customers\" (\"customer_id\") ON DELETE CASCADE"));
        String down = PostgresSqlGenerator.down(ops);
        assertTrue(down.indexOf("DROP CONSTRAINT") < down.indexOf("DROP EXTENSION"));
    }

    @Test
    void diffsDefaultsAndConstraintsAndCanReverseThem() {
        var fk = new ForeignKeySchema("fk_orders_customer", List.of("customer_id"), "customers", List.of("customer_id"), ForeignKeySchema.DeleteAction.RESTRICT, null);
        var before = new TableSchema("orders", List.of(new ColumnSchema("customer_id", "integer", false, null, null)), List.of(), List.of());
        var after = new TableSchema("orders", List.of(new ColumnSchema("customer_id", "integer", false, "0", null)), List.of(), List.of(), List.of(fk), List.of(new CheckConstraintSchema("ck_customer", "customer_id > 0")));
        var ops = SchemaDiffer.diff(new DatabaseSchema(List.of(before)), new DatabaseSchema(List.of(after)));
        assertTrue(ops.stream().anyMatch(Operation.SetColumnDefault.class::isInstance));
        assertTrue(ops.stream().anyMatch(Operation.AddForeignKey.class::isInstance));
        assertTrue(ops.stream().anyMatch(Operation.AddCheck.class::isInstance));
        String down = PostgresSqlGenerator.down(ops);
        assertTrue(down.contains("DROP CONSTRAINT \"fk_orders_customer\""));
        assertTrue(down.contains("DROP DEFAULT"));
        assertTrue(down.contains("DROP CONSTRAINT \"ck_customer\""));
    }

    @Test
    void createsReferencedTablesBeforeAddingForeignKeysAndRestoresDropsSafely() {
        var parent = new TableSchema("parents", List.of(new ColumnSchema("id", "integer", false)), List.of("id"), List.of());
        var fk = new ForeignKeySchema("fk_children_parent", List.of("parent_id"), "parents", List.of("id"), ForeignKeySchema.DeleteAction.CASCADE, null);
        var child = new TableSchema("children", List.of(new ColumnSchema("id", "integer", false), new ColumnSchema("parent_id", "integer", false)), List.of("id"), List.of(), List.of(fk), List.of());
        var model = new DatabaseSchema(List.of(child, parent));
        String up = PostgresSqlGenerator.up(SchemaDiffer.diff(DatabaseSchema.empty(), model));
        assertTrue(Math.max(up.indexOf("CREATE TABLE \"parents\""), up.indexOf("CREATE TABLE \"children\"")) < up.indexOf("ADD CONSTRAINT \"fk_children_parent\""));
        String down = PostgresSqlGenerator.down(SchemaDiffer.diff(model, DatabaseSchema.empty()));
        assertTrue(down.indexOf("CREATE TABLE \"parents\"") < down.indexOf("ADD CONSTRAINT \"fk_children_parent\""));
        assertTrue(down.indexOf("CREATE TABLE \"children\"") < down.indexOf("ADD CONSTRAINT \"fk_children_parent\""));
    }

    @Test
    void schemaSnapshotRoundTripsPostgresSpecificMetadata() {
        var table = new TableSchema("items", List.of(new ColumnSchema("id", "bigint", false, null, IdentityGeneration.ALWAYS)), List.of("id"),
                List.of(new IndexSchema("ix_items_search", List.of("search_text"), false, "gin", "gin_trgm_ops")),
                List.of(new ForeignKeySchema("fk_items_parent", List.of("parent_id"), "parents", List.of("id"), ForeignKeySchema.DeleteAction.CASCADE, null)),
                List.of(new CheckConstraintSchema("ck_items", "id > 0")));
        var schema = new DatabaseSchema(List.of(table), List.of(new PostgresExtensionSchema("pg_trgm")));
        assertEquals(schema, DatabaseSchema.fromJson(schema.toJson()));
        // Legacy snapshots omit extension/constraint/default/identity fields and still load with empty defaults.
        var legacy = DatabaseSchema.fromJson("{\"tables\":[{\"name\":\"legacy\",\"columns\":[{\"name\":\"id\",\"type\":\"integer\",\"nullable\":false}],\"primaryKey\":[\"id\"],\"indexes\":[]}]} ");
        assertTrue(legacy.extensions().isEmpty());
        assertTrue(legacy.tables().get(0).foreignKeys().isEmpty());
    }

    @Test
    void migrationFilesSortByNumericVersion() {
        var a = MigrationFile.parse(Path.of("V2__a.sql")).orElseThrow();
        var b = MigrationFile.parse(Path.of("V10__b.sql")).orElseThrow();
        assertTrue(a.compareVersion(b) < 0);
        assertTrue(MigrationFile.parse(Path.of("R__demo.sql")).isEmpty());
    }
}
