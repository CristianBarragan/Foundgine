package com.foundgine.samples.supplychain.migrations;

import com.foundgine.migrations.diff.SchemaDiffer;
import com.foundgine.migrations.schema.DatabaseSchema;
import com.foundgine.migrations.schema.SchemaBuilder;
import com.foundgine.migrations.sql.PostgresSqlGenerator;
import com.foundgine.samples.supplychain.model.SimplePhysicalSchemaModel;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

/** Generates the starter sample's Flyway baseline from its authoritative Java fluent model. */
public final class SimpleMigrationGenerator {
    public static final String DEFAULT_FILE_NAME = "V1__supply_chain_simple.generated.sql";

    private SimpleMigrationGenerator() {}

    /** Returns deterministic PostgreSQL DDL for the starter's physical model. */
    public static String generateV1() {
        DatabaseSchema target = SchemaBuilder.fromMetadata(new SimplePhysicalSchemaModel().build());
        return PostgresSqlGenerator.up(SchemaDiffer.diff(DatabaseSchema.empty(), target));
    }

    /** Writes generated DDL to the explicit path without touching the checked-in Flyway migration. */
    public static Path writeV1(Path output) throws IOException {
        Path absolute = output.toAbsolutePath().normalize();
        Path parent = absolute.getParent();
        if (parent != null) Files.createDirectories(parent);
        Files.writeString(absolute, generateV1(), StandardCharsets.UTF_8);
        return absolute;
    }

    /** Usage: SimpleMigrationGenerator [output.sql]. */
    public static void main(String[] args) throws IOException {
        if (args.length > 1) {
            throw new IllegalArgumentException("Usage: SimpleMigrationGenerator [output.sql]");
        }
        Path output = args.length == 1
                ? Path.of(args[0])
                : Path.of("target", "generated-migrations", DEFAULT_FILE_NAME);
        System.out.println("Generated simple Supply Chain V1 migration: " + writeV1(output));
    }
}
