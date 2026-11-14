package com.foundgine.migrations.schema;

import java.util.List;
import java.util.Optional;

/** Physical table definition, including relational constraints. */
public record TableSchema(String name, List<ColumnSchema> columns, List<String> primaryKey, List<IndexSchema> indexes, List<ForeignKeySchema> foreignKeys, List<CheckConstraintSchema> checks) {
    public TableSchema(String name, List<ColumnSchema> columns, List<String> primaryKey, List<IndexSchema> indexes) { this(name, columns, primaryKey, indexes, List.of(), List.of()); }
    public TableSchema {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Table name is required.");
        columns = List.copyOf(columns); primaryKey = primaryKey == null ? List.of() : List.copyOf(primaryKey);
        indexes = indexes == null ? List.of() : List.copyOf(indexes);
        foreignKeys = foreignKeys == null ? List.of() : List.copyOf(foreignKeys);
        checks = checks == null ? List.of() : List.copyOf(checks);
    }
    public Optional<ColumnSchema> column(String columnName) { return columns.stream().filter(c -> c.name().equals(columnName)).findFirst(); }
    public Optional<IndexSchema> index(String indexName) { return indexes.stream().filter(i -> i.name().equals(indexName)).findFirst(); }
    public Optional<ForeignKeySchema> foreignKey(String constraintName) { return foreignKeys.stream().filter(f -> f.name().equals(constraintName)).findFirst(); }
    public Optional<CheckConstraintSchema> check(String constraintName) { return checks.stream().filter(c -> c.name().equals(constraintName)).findFirst(); }
}
