package com.foundgine.core.semantic.metadata;

import com.foundgine.core.abstractions.ColumnId;

/**
 * A physical column. {@code maxLength} and {@code required} come from persistence
 * configuration and are null/false when unconstrained or unknown.
 */
public record ColumnMetadata(
        ColumnId id,
        String name,
        String storageName,
        Integer maxLength,
        boolean required,
        Class<?> clrType,
        String sqlType,
        Integer precision,
        Integer scale, String defaultExpression, String identityGeneration) {
    public ColumnMetadata(
            ColumnId id, String name, String storageName, Integer maxLength, boolean required) {
        this(id, name, storageName, maxLength, required, null, null, null, null, null, null);
    }

    public ColumnMetadata(ColumnId id, String name, String storageName) {
        this(id, name, storageName, null, false);
    }

    public ColumnMetadata(ColumnId id, String name) {
        this(id, name, null, null, false, null, null, null, null, null, null);
    }

    public ColumnMetadata(ColumnId id, String name, String storageName, Integer maxLength, boolean required, Class<?> clrType, String sqlType, Integer precision, Integer scale) {
        this(id, name, storageName, maxLength, required, clrType, sqlType, precision, scale, null, null);
    }

    public String effectiveStorageName() {
        return storageName != null ? storageName : name;
    }
}
