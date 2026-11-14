package com.foundgine.migrations.schema;

/** PostgreSQL extension required by the schema. */
public record PostgresExtensionSchema(String name, String schema) {
    public PostgresExtensionSchema(String name) { this(name, null); }
    public PostgresExtensionSchema {
        if (name == null || !name.matches("[A-Za-z_][A-Za-z0-9_$]*")) throw new IllegalArgumentException("Invalid PostgreSQL extension name: " + name);
        if (schema != null && !schema.matches("[A-Za-z_][A-Za-z0-9_$]*")) throw new IllegalArgumentException("Invalid extension schema: " + schema);
    }
}
