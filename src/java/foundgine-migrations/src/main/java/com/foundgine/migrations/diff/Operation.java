package com.foundgine.migrations.diff;

import com.foundgine.migrations.schema.*;

/** One schema change. warning() identifies operations that may fail or lose data. */
public sealed interface Operation { String warning();
    record CreateTable(TableSchema table) implements Operation { public String warning() { return null; } }
    record DropTable(TableSchema table) implements Operation { public String warning() { return "drops table " + table.name() + " and all of its rows"; } }
    record AddColumn(String table, ColumnSchema column) implements Operation { public String warning() { return (column.nullable() || column.defaultExpression() != null || column.identity() != null) ? null : "adds NOT NULL column " + table + "." + column.name() + "; this fails if the table already has rows unless it has a usable default"; } }
    record DropColumn(String table, ColumnSchema column) implements Operation { public String warning() { return "drops column " + table + "." + column.name() + " and its data"; } }
    record AlterColumnType(String table, String column, String from, String to) implements Operation { public String warning() { return "changes " + table + "." + column + " from " + from + " to " + to + "; values that do not convert make it fail or lose data"; } }
    record AlterColumnNullability(String table, String column, boolean nullable) implements Operation { public String warning() { return nullable ? null : "makes " + table + "." + column + " NOT NULL; this fails if any row holds NULL"; } }
    record SetColumnDefault(String table, String column, String fromExpression, String expression) implements Operation { public String warning() { return null; } }
    record CreateIndex(String table, IndexSchema index) implements Operation { public String warning() { return index.unique() ? "creates unique index " + index.name() + "; this fails if existing rows are duplicated" : null; } }
    record DropIndex(String table, IndexSchema index) implements Operation { public String warning() { return null; } }
    record AddForeignKey(String table, ForeignKeySchema foreignKey) implements Operation { public String warning() { return "adds foreign key " + foreignKey.name() + "; existing orphan rows will make this fail"; } }
    record DropForeignKey(String table, ForeignKeySchema foreignKey) implements Operation { public String warning() { return null; } }
    record AddCheck(String table, CheckConstraintSchema check) implements Operation { public String warning() { return "adds CHECK constraint " + check.name() + "; existing rows violating it will make this fail"; } }
    record DropCheck(String table, CheckConstraintSchema check) implements Operation { public String warning() { return null; } }
    record CreateExtension(PostgresExtensionSchema extension) implements Operation { public String warning() { return null; } }
    record DropExtension(PostgresExtensionSchema extension) implements Operation { public String warning() { return "drops PostgreSQL extension " + extension.name() + " and may remove dependent objects"; } }
}
