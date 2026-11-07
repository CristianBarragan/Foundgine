package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.ColumnDefinition;

public final class FoundginePropertyBuilder {
    private final ColumnDefinition column;

    FoundginePropertyBuilder(ColumnDefinition column) {
        this.column = column;
    }

    public FoundginePropertyBuilder hasColumnName(String name) {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Column name is required.");
        column.columnName = name.trim();
        return this;
    }

    public FoundginePropertyBuilder hasMaxLength(int length) {
        if (length <= 0) throw new IllegalArgumentException("Max length must be positive.");
        column.maxLength = length;
        return this;
    }

    public FoundginePropertyBuilder isRequired() {
        return isRequired(true);
    }

    public FoundginePropertyBuilder isRequired(boolean required) {
        column.required = required;
        return this;
    }
}
