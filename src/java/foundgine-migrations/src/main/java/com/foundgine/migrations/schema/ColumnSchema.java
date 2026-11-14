package com.foundgine.migrations.schema;

/** Physical column definition. SQL expressions are trusted model-authored SQL, never user input. */
public record ColumnSchema(String name, String type, boolean nullable, String defaultExpression, IdentityGeneration identity) {
    public ColumnSchema(String name, String type, boolean nullable) { this(name, type, nullable, null, null); }
    public ColumnSchema {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Column name is required.");
        if (type == null || type.isBlank()) throw new IllegalArgumentException("Column type is required for " + name + ".");
        type = type.trim();
        if (defaultExpression != null && defaultExpression.isBlank()) defaultExpression = null;
        if (identity != null && defaultExpression != null) throw new IllegalArgumentException("Identity column " + name + " cannot also have a default expression.");
        if (identity != null && !java.util.Set.of("smallint", "integer", "bigint").contains(type.toLowerCase(java.util.Locale.ROOT)))
            throw new IllegalArgumentException("PostgreSQL identity column " + name + " must use smallint, integer, or bigint.");
    }
    public String normalizedType() { return type.toLowerCase(java.util.Locale.ROOT).replace(" ", ""); }
}
