package com.foundgine.core.semantic.metadata;

import com.foundgine.core.abstractions.ColumnId;

/**
 * A physical column. {@code maxLength} and {@code required} come from persistence
 * configuration and are null/false when unconstrained or unknown.
 */
public record ColumnMetadata(
        ColumnId id, String name, String storageName, Integer maxLength, boolean required) {
    public ColumnMetadata(ColumnId id, String name, String storageName) {
        this(id, name, storageName, null, false);
    }

    public ColumnMetadata(ColumnId id, String name) {
        this(id, name, null, null, false);
    }

    public String effectiveStorageName() {
        return storageName != null ? storageName : name;
    }
}
