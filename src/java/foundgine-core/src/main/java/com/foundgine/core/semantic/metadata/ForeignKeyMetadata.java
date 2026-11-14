package com.foundgine.core.semantic.metadata;

import java.util.List;

/** Provider-independent physical foreign-key declaration. Referential actions use PostgreSQL-compatible names. */
public record ForeignKeyMetadata(String name, List<String> columns, String referencedTable, List<String> referencedColumns, String onDelete, String onUpdate) {
    public ForeignKeyMetadata {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Foreign-key name is required.");
        if (columns == null || columns.isEmpty()) throw new IllegalArgumentException("Foreign key needs local columns.");
        if (referencedTable == null || referencedTable.isBlank()) throw new IllegalArgumentException("Referenced table is required.");
        if (referencedColumns == null || referencedColumns.isEmpty() || columns.size() != referencedColumns.size()) throw new IllegalArgumentException("Foreign key needs matching local and referenced columns.");
        columns = List.copyOf(columns); referencedColumns = List.copyOf(referencedColumns);
        onDelete = onDelete == null ? "NO_ACTION" : onDelete; onUpdate = onUpdate == null ? "NO_ACTION" : onUpdate;
    }
}
