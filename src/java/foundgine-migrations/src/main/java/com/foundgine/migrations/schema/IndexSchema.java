package com.foundgine.migrations.schema;

import java.util.List;

/** PostgreSQL index. method defaults to btree; operatorClass is optional (for example gin_trgm_ops). */
public record IndexSchema(String name, List<String> columns, boolean unique, String method, String operatorClass) {
    public IndexSchema(String name, List<String> columns, boolean unique) { this(name, columns, unique, "btree", null); }
    public IndexSchema {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Index name is required.");
        if (columns == null || columns.isEmpty()) throw new IllegalArgumentException("Index " + name + " needs columns.");
        columns = List.copyOf(columns); method = method == null || method.isBlank() ? "btree" : method.toLowerCase(java.util.Locale.ROOT);
        if (!method.matches("[a-z][a-z0-9_]*")) throw new IllegalArgumentException("Invalid PostgreSQL index method: " + method);
        if (operatorClass != null && !operatorClass.matches("[A-Za-z_][A-Za-z0-9_$.]*")) throw new IllegalArgumentException("Invalid PostgreSQL operator class: " + operatorClass);
    }
}
