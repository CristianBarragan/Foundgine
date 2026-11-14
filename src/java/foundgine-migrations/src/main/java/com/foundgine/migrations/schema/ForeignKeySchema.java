package com.foundgine.migrations.schema;

import java.util.List;

/** A named FK from local columns to a referenced table's columns. */
public record ForeignKeySchema(String name, List<String> columns, String referencedTable, List<String> referencedColumns, DeleteAction onDelete, DeleteAction onUpdate) {
    public enum DeleteAction { NO_ACTION, RESTRICT, CASCADE, SET_NULL, SET_DEFAULT }
    public ForeignKeySchema {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Foreign-key name is required.");
        if (columns == null || columns.isEmpty()) throw new IllegalArgumentException("Foreign key " + name + " needs local columns.");
        if (referencedTable == null || referencedTable.isBlank()) throw new IllegalArgumentException("Referenced table is required.");
        if (referencedColumns == null || referencedColumns.isEmpty() || columns.size() != referencedColumns.size()) throw new IllegalArgumentException("Foreign key " + name + " needs matching local and referenced columns.");
        columns = List.copyOf(columns); referencedColumns = List.copyOf(referencedColumns);
        onDelete = onDelete == null ? DeleteAction.NO_ACTION : onDelete;
        onUpdate = onUpdate == null ? DeleteAction.NO_ACTION : onUpdate;
    }
}
