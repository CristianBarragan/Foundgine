package com.foundgine.core.semantic.metadata;

/** Named model-authored CHECK expression. */
public record CheckConstraintMetadata(String name, String expression) {
    public CheckConstraintMetadata {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("CHECK constraint name is required.");
        if (expression == null || expression.isBlank()) throw new IllegalArgumentException("CHECK expression is required.");
        expression = expression.trim();
    }
}
