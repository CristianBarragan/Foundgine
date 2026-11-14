package com.foundgine.samples.supplychain.advanced.migrations;

import static org.junit.jupiter.api.Assertions.*;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class AdvancedMigrationGeneratorTest {
    @TempDir Path temporaryDirectory;

    @Test
    void generatedMigrationIsDeterministicAndContainsThePhysicalSchemaContract() {
        String first = AdvancedMigrationGenerator.generateV1();
        String second = AdvancedMigrationGenerator.generateV1();

        assertEquals(first, second);
        assertTrue(first.contains("CREATE EXTENSION IF NOT EXISTS \"pg_trgm\""));
        assertTrue(first.contains("GENERATED ALWAYS AS IDENTITY"));
        assertTrue(first.contains("DEFAULT 0"));
        assertTrue(first.contains("gin_trgm_ops"));
        assertTrue(first.contains("FOREIGN KEY"));
        assertTrue(first.contains("CHECK ("));
        assertFalse(first.contains("semantic_"), "Semantic/planning placeholders must not leak into V1");
    }

    @Test
    void writesOnlyToTheExplicitOutputPath() throws Exception {
        Path output = temporaryDirectory.resolve("nested/V1__generated.sql");
        Path written = AdvancedMigrationGenerator.writeV1(output);

        assertEquals(output.toAbsolutePath().normalize(), written);
        assertEquals(AdvancedMigrationGenerator.generateV1(), Files.readString(written));
    }
}
