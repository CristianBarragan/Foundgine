package com.foundgine.migrations.schema;

/** Named CHECK expression authored as PostgreSQL SQL. */
public record CheckConstraintSchema(String name, String expression) {
    public CheckConstraintSchema {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("CHECK constraint name is required.");
        if (expression == null || expression.isBlank()) throw new IllegalArgumentException("CHECK expression is required for " + name + ".");
        expression = expression.trim();
    }
}
