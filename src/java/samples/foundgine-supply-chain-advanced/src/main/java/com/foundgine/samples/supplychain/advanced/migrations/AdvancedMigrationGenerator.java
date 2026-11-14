package com.foundgine.samples.supplychain.advanced.migrations;

import com.foundgine.migrations.diff.SchemaDiffer;
import com.foundgine.migrations.schema.DatabaseSchema;
import com.foundgine.migrations.schema.SchemaBuilder;
import com.foundgine.migrations.sql.PostgresSqlGenerator;
import com.foundgine.samples.supplychain.advanced.model.AdvancedPhysicalSchemaModel;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * Generates the Advanced sample's initial Flyway migration from its authoritative Java fluent
 * physical model. This deliberately writes to a caller-selected path: it never overwrites the
 * checked-in V1 migration implicitly.
 */
public final class AdvancedMigrationGenerator {
    public static final String DEFAULT_FILE_NAME = "V1__supply_chain_advanced.generated.sql";

    private AdvancedMigrationGenerator() {}

    /** Returns deterministic PostgreSQL DDL for the current Advanced physical model. */
    public static String generateV1() {
        DatabaseSchema target = SchemaBuilder.fromMetadata(new AdvancedPhysicalSchemaModel().build());
        return PostgresSqlGenerator.up(SchemaDiffer.diff(DatabaseSchema.empty(), target));
    }

    /** Writes generated DDL to {@code output}, creating its parent directory when necessary. */
    public static Path writeV1(Path output) throws IOException {
        Path absolute = output.toAbsolutePath().normalize();
        Path parent = absolute.getParent();
        if (parent != null) Files.createDirectories(parent);
        Files.writeString(absolute, generateV1(), StandardCharsets.UTF_8);
        return absolute;
    }

    /**
     * Usage: AdvancedMigrationGenerator [output.sql]. The default output is
     * target/generated-migrations/V1__supply_chain_advanced.generated.sql.
     */
    public static void main(String[] args) throws IOException {
        if (args.length > 1) {
            throw new IllegalArgumentException("Usage: AdvancedMigrationGenerator [output.sql]");
        }
        Path output = args.length == 1
                ? Path.of(args[0])
                : Path.of("target", "generated-migrations", DEFAULT_FILE_NAME);
        System.out.println("Generated Advanced V1 migration: " + writeV1(output));
    }
}
