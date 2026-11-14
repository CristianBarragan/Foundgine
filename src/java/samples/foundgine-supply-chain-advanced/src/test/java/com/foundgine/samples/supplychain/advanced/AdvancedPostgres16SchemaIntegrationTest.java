package com.foundgine.samples.supplychain.advanced;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.migrations.schema.DatabaseSchema;
import com.foundgine.migrations.sql.PostgresSqlGenerator;
import com.foundgine.migrations.schema.SchemaBuilder;
import com.foundgine.migrations.diff.SchemaDiffer;
import com.foundgine.samples.supplychain.advanced.model.AdvancedPhysicalSchemaModel;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.Statement;
import java.nio.charset.StandardCharsets;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.Test;

/**
 * Opt-in PostgreSQL 16 integration test for the SQL emitted by the fluent Advanced model.
 *
 * Set FOUNDGINE_POSTGRES16_JDBC_URL (and optionally FOUNDGINE_POSTGRES16_USER and
 * FOUNDGINE_POSTGRES16_PASSWORD) to point at a disposable PostgreSQL 16 database. The test
 * creates isolated reference/generated schemas, applies both SQL sources, compares PostgreSQL
 * catalog semantics, then drops the schemas. pg_trgm is installed at database scope and retained.
 */
class AdvancedPostgres16SchemaIntegrationTest {
    @Test
    void generatedFluentSchemaAppliesAndMatchesHandwrittenV1Catalog() throws Exception {
        String url = System.getenv("FOUNDGINE_POSTGRES16_JDBC_URL");
        Assumptions.assumeTrue(url != null && !url.isBlank(),
                "Set FOUNDGINE_POSTGRES16_JDBC_URL to enable the PostgreSQL 16 integration test");

        String user = System.getenv("FOUNDGINE_POSTGRES16_USER");
        String password = System.getenv("FOUNDGINE_POSTGRES16_PASSWORD");
        String suffix = UUID.randomUUID().toString().replace("-", "");
        String referenceSchema = "fg_cp13_ref_" + suffix;
        String generatedSchema = "fg_cp13_gen_" + suffix;
        DatabaseSchema model = SchemaBuilder.fromMetadata(new AdvancedPhysicalSchemaModel().build());
        String generated = PostgresSqlGenerator.up(SchemaDiffer.diff(DatabaseSchema.empty(), model));
        String handwritten = readHandwrittenV1();

        try (Connection connection = DriverManager.getConnection(url, user, password);
             Statement statement = connection.createStatement()) {
            int serverVersion;
            try (ResultSet rs = statement.executeQuery("SHOW server_version_num")) {
                assertTrue(rs.next());
                serverVersion = Integer.parseInt(rs.getString(1));
            }
            assertTrue(serverVersion >= 160000 && serverVersion < 170000,
                    "Expected PostgreSQL 16, got server_version_num=" + serverVersion);

            // pg_trgm is database-scoped. Install it in public once so both isolated schemas
            // resolve the same extension/operator class rather than accidentally comparing
            // extension placement as part of the application schema.
            statement.execute("CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public");
            statement.execute("CREATE SCHEMA \"" + referenceSchema + "\"");
            statement.execute("CREATE SCHEMA \"" + generatedSchema + "\"");
            try {
                applyInSchema(statement, referenceSchema, handwritten);
                applyInSchema(statement, generatedSchema, generated);

                List<String> referenceColumns = catalogRows(connection, referenceSchema,
                        "SELECT table_name, ordinal_position, column_name, data_type, udt_name, "
                        + "COALESCE(character_maximum_length,-1), COALESCE(numeric_precision,-1), "
                        + "COALESCE(numeric_scale,-1), is_nullable, COALESCE(column_default,''), "
                        + "is_identity, COALESCE(identity_generation,''), "
                        + "COALESCE(collation_name,''), is_generated, COALESCE(generation_expression,'') "
                        + "FROM information_schema.columns WHERE table_schema=? "
                        + "ORDER BY table_name, ordinal_position");
                List<String> generatedColumns = catalogRows(connection, generatedSchema,
                        "SELECT table_name, ordinal_position, column_name, data_type, udt_name, "
                        + "COALESCE(character_maximum_length,-1), COALESCE(numeric_precision,-1), "
                        + "COALESCE(numeric_scale,-1), is_nullable, COALESCE(column_default,''), "
                        + "is_identity, COALESCE(identity_generation,''), "
                        + "COALESCE(collation_name,''), is_generated, COALESCE(generation_expression,'') "
                        + "FROM information_schema.columns WHERE table_schema=? "
                        + "ORDER BY table_name, ordinal_position");
                assertEquals(referenceColumns, generatedColumns, "Column catalog differs from handwritten V1");

                // Compare constraint semantics rather than generated names: PostgreSQL assigns
                // different default names to equivalent inline V1 declarations and fluent named
                // declarations. Unique constraints are represented by their unique indexes in
                // the model API, so they are compared in the index catalog below.
                String constraintsSql = "SELECT t.relname, c.contype, c.convalidated, "
                        + "c.condeferrable, c.condeferred, c.conislocal, c.coninhcount, "
                        + "CASE WHEN c.contype='f' THEN c.confmatchtype::text ELSE '' END, "
                        + "CASE WHEN c.contype='f' THEN c.confupdtype::text ELSE '' END, "
                        + "CASE WHEN c.contype='f' THEN c.confdeltype::text ELSE '' END, "
                        + "pg_get_constraintdef(c.oid, true) FROM pg_constraint c "
                        + "JOIN pg_class t ON t.oid=c.conrelid "
                        + "JOIN pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname=? AND c.contype <> 'u' "
                        + "ORDER BY t.relname, c.contype, pg_get_constraintdef(c.oid, true)";
                assertEquals(catalogRows(connection, referenceSchema, constraintsSql),
                        catalogRows(connection, generatedSchema, constraintsSql),
                        "Constraint catalog differs from handwritten V1");

                String indexesSql = "SELECT t.relname, i.relname, x.indisunique, x.indisprimary, "
                        + "x.indisvalid, x.indisready, x.indislive, x.indnkeyatts, x.indnatts, "
                        + "x.indnullsnotdistinct, "
                        + "replace(replace(pg_get_indexdef(x.indexrelid), '\"' || n.nspname || '\".', ''), n.nspname || '.', '') "
                        + "FROM pg_index x JOIN pg_class t ON t.oid=x.indrelid "
                        + "JOIN pg_class i ON i.oid=x.indexrelid "
                        + "JOIN pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname=? "
                        + "ORDER BY t.relname, i.relname";
                assertEquals(catalogRows(connection, referenceSchema, indexesSql),
                        catalogRows(connection, generatedSchema, indexesSql),
                        "Index catalog differs from handwritten V1");

                String tablesSql = "SELECT c.relname, c.relpersistence, c.relkind, "
                        + "c.relrowsecurity, c.relforcerowsecurity FROM pg_class c "
                        + "JOIN pg_namespace n ON n.oid=c.relnamespace "
                        + "WHERE n.nspname=? AND c.relkind IN ('r','p') ORDER BY c.relname";
                assertEquals(catalogRows(connection, referenceSchema, tablesSql),
                        catalogRows(connection, generatedSchema, tablesSql),
                        "Table storage/security catalog differs from handwritten V1");

                assertEquals(11, scalar(statement,
                        "SELECT count(*) FROM information_schema.tables WHERE table_schema='" + generatedSchema + "' AND table_type='BASE TABLE'"));
                assertEquals(1, scalar(statement,
                        "SELECT count(*) FROM pg_extension WHERE extname='pg_trgm'"));
                assertEquals(1, scalar(statement,
                        "SELECT count(*) FROM pg_indexes WHERE schemaname='" + generatedSchema + "' AND indexname='ix_suppliers_name_trgm' AND indexdef LIKE '%USING gin%' AND indexdef LIKE '%gin_trgm_ops%'"));
                assertEquals(1, scalar(statement,
                        "SELECT count(*) FROM pg_extension e JOIN pg_namespace n ON n.oid=e.extnamespace "
                        + "WHERE e.extname='pg_trgm' AND n.nspname='public'"),
                        "pg_trgm must be installed in the expected public schema");
            } finally {
                statement.execute("DROP SCHEMA IF EXISTS \"" + referenceSchema + "\" CASCADE");
                statement.execute("DROP SCHEMA IF EXISTS \"" + generatedSchema + "\" CASCADE");
            }
        }
    }

    private static String readHandwrittenV1() throws Exception {
        try (InputStream input = AdvancedPostgres16SchemaIntegrationTest.class
                .getResourceAsStream("/db/migration/V1__supply_chain_advanced.sql")) {
            assertNotNull(input, "Checked-in Advanced V1 migration must be on the test classpath");
            return new String(input.readAllBytes(), StandardCharsets.UTF_8);
        }
    }

    private static void applyInSchema(Statement statement, String schema, String sql) throws Exception {
        statement.execute("SET search_path TO \"" + schema + "\", public");
        try {
            for (String part : sql.split(";")) {
                String command = part.strip();
                if (command.isEmpty() || command.toUpperCase().contains("CREATE EXTENSION")) continue;
                statement.execute(command);
            }
        } finally {
            statement.execute("RESET search_path");
        }
    }

    private static List<String> catalogRows(Connection connection, String schema, String sql) throws Exception {
        List<String> rows = new ArrayList<>();
        try (PreparedStatement statement = connection.prepareStatement(sql)) {
            statement.setString(1, schema);
            try (ResultSet result = statement.executeQuery()) {
                int count = result.getMetaData().getColumnCount();
                while (result.next()) {
                    StringBuilder row = new StringBuilder();
                    for (int column = 1; column <= count; column++) {
                        if (column > 1) row.append('\u001f');
                        row.append(result.getString(column) == null ? "<NULL>" : result.getString(column));
                    }
                    rows.add(row.toString());
                }
            }
        }
        return rows;
    }

    private static int scalar(Statement statement, String sql) throws Exception {
        try (ResultSet rs = statement.executeQuery(sql)) {
            assertTrue(rs.next(), "No result for: " + sql);
            return rs.getInt(1);
        }
    }
}
