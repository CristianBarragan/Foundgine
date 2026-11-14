package com.foundgine.samples.supplychain.jpa;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.migrations.MigrationsService;
import com.foundgine.migrations.MigrationsWorkspace;
import com.foundgine.migrations.schema.SchemaBuilder;
import com.foundgine.samples.supplychain.model.SupplyChainFoundgineModel;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

/** EF's "pending model changes" check: the fluent model must match the last generated migration. */
class ModelMigrationsTest {
    private static final MigrationsService SERVICE =
            new MigrationsService(MigrationsWorkspace.forProject(Path.of("").toAbsolutePath()));

    @Test
    void modelHasNoChangesThatNoMigrationCovers() {
        var model = SchemaBuilder.fromMetadata(new SupplyChainFoundgineModel().build());
        assertFalse(
                SERVICE.hasPendingModelChanges(model),
                "The fluent model changed without a migration. Run: ./migrations.sh migrations add <Name>");
    }

    @Test
    void initialMigrationCreatesTheThreeTables() {
        var sql = SERVICE.script(null);
        for (String table : new String[] {"suppliers", "warehouses", "products"})
            assertTrue(sql.contains("CREATE TABLE \"" + table + "\""), table);
    }
}
