package com.foundgine.samples.supplychain;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.samples.supplychain.migrations.SimpleMigrationGenerator;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class SimpleMigrationGeneratorTest {
    @TempDir Path temporaryDirectory;

    @Test
    void generatedMigrationIsDeterministicAndContainsOnlyStarterTables() {
        String first = SimpleMigrationGenerator.generateV1();
        assertEquals(first, SimpleMigrationGenerator.generateV1());
        assertTrue(first.contains("CREATE TABLE \"products\""));
        assertTrue(first.contains("CREATE TABLE \"suppliers\""));
        assertTrue(first.contains("CREATE TABLE \"warehouses\""));
        assertTrue(first.contains("CREATE UNIQUE INDEX \"ux_products_sku\""));
        assertFalse(first.contains("inventory_lots"));
        assertFalse(first.contains("orders"));
    }

    @Test
    void writesToExplicitPathAndDoesNotRequireReplacingCheckedInMigration() throws Exception {
        Path output = temporaryDirectory.resolve("generated/V1__simple.sql");
        Path written = SimpleMigrationGenerator.writeV1(output);
        assertEquals(output.toAbsolutePath().normalize(), written);
        assertEquals(SimpleMigrationGenerator.generateV1(), Files.readString(written));
    }
}
