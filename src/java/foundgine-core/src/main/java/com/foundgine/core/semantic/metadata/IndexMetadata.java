package com.foundgine.core.semantic.metadata;

import java.util.List;

/** A declared physical index, including PostgreSQL access method and optional operator class. */
public record IndexMetadata(String name, List<String> columns, boolean unique, String method, String operatorClass) {
    public IndexMetadata(String name, List<String> columns, boolean unique) { this(name, columns, unique, "btree", null); }
    public IndexMetadata {
        columns = List.copyOf(columns);
        method = method == null || method.isBlank() ? "btree" : method.toLowerCase(java.util.Locale.ROOT);
        if (!method.matches("[a-z][a-z0-9_]*")) throw new IllegalArgumentException("Invalid index method: " + method);
        if (operatorClass != null && !operatorClass.matches("[A-Za-z_][A-Za-z0-9_$.]*")) throw new IllegalArgumentException("Invalid operator class: " + operatorClass);
    }
}
