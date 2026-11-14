package com.foundgine.migrations.sql;

import com.foundgine.migrations.diff.Operation;
import com.foundgine.migrations.diff.Operation.*;
import com.foundgine.migrations.schema.*;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.stream.Collectors;

/** Renders operations as PostgreSQL. Identifiers are always quoted, the same way the SQL provider quotes them. */
public final class PostgresSqlGenerator {
    private PostgresSqlGenerator() {}

    /** SQL that applies the operations, one statement per line group, with a comment above risky ones. */
    public static String up(List<Operation> operations) {
        StringBuilder sql = new StringBuilder();
        for (Operation operation : operations) {
            if (operation.warning() != null) sql.append("-- WARNING: ").append(operation.warning()).append('\n');
            sql.append(render(operation)).append("\n\n");
        }
        return sql.toString().stripTrailing() + "\n";
    }

    /** SQL that reverses the operations, in reverse order. It is generated for review and manual use. */
    public static String down(List<Operation> operations) {
        List<Operation> inverse = new ArrayList<>();
        for (Operation operation : operations) inverse.add(invert(operation));
        Collections.reverse(inverse);
        StringBuilder sql = new StringBuilder();
        List<String> restoredForeignKeys = new ArrayList<>();
        for (int i = 0; i < inverse.size(); i++) {
            Operation original = operations.get(operations.size() - 1 - i);
            Operation operation = inverse.get(i);
            if (original instanceof DropTable dropped) {
                sql.append(createTable(dropped.table())).append("\n");
                for (ForeignKeySchema fk : dropped.table().foreignKeys()) restoredForeignKeys.add(addForeignKey(dropped.table().name(), fk));
            } else sql.append(render(operation)).append("\n");
            sql.append("\n");
        }
        for (String foreignKey : restoredForeignKeys) sql.append(foreignKey).append("\n\n");
        return sql.toString().stripTrailing() + "\n";
    }

    static Operation invert(Operation operation) {
        return switch (operation) {
            case CreateTable o -> new DropTable(o.table());
            case DropTable o -> new CreateTable(o.table());
            case AddColumn o -> new DropColumn(o.table(), o.column());
            case DropColumn o -> new AddColumn(o.table(), o.column());
            case AlterColumnType o -> new AlterColumnType(o.table(), o.column(), o.to(), o.from());
            case AlterColumnNullability o -> new AlterColumnNullability(o.table(), o.column(), !o.nullable());
            case CreateIndex o -> new DropIndex(o.table(), o.index());
            case DropIndex o -> new CreateIndex(o.table(), o.index());
            case SetColumnDefault o -> new SetColumnDefault(o.table(), o.column(), o.expression(), o.fromExpression());
            case AddForeignKey o -> new DropForeignKey(o.table(), o.foreignKey());
            case DropForeignKey o -> new AddForeignKey(o.table(), o.foreignKey());
            case AddCheck o -> new DropCheck(o.table(), o.check());
            case DropCheck o -> new AddCheck(o.table(), o.check());
            case CreateExtension o -> new DropExtension(o.extension());
            case DropExtension o -> new CreateExtension(o.extension());
        };
    }

    static String render(Operation operation) {
        return switch (operation) {
            case CreateTable o -> createTable(o.table());
            case DropTable o -> "DROP TABLE " + qualified(o.table().name()) + ";";
            case AddColumn o ->
                    "ALTER TABLE " + qualified(o.table()) + " ADD COLUMN " + column(o.column()) + ";";
            case DropColumn o ->
                    "ALTER TABLE " + qualified(o.table()) + " DROP COLUMN " + quote(o.column().name()) + ";";
            case AlterColumnType o ->
                    "ALTER TABLE " + qualified(o.table()) + " ALTER COLUMN " + quote(o.column()) + " TYPE " + o.to()
                            + " USING " + quote(o.column()) + "::" + o.to() + ";";
            case AlterColumnNullability o ->
                    "ALTER TABLE " + qualified(o.table()) + " ALTER COLUMN " + quote(o.column())
                            + (o.nullable() ? " DROP NOT NULL;" : " SET NOT NULL;");
            case CreateIndex o -> createIndex(o.table(), o.index());
            case DropIndex o -> "DROP INDEX " + schemaOf(o.table()) + quote(o.index().name()) + ";";
            case SetColumnDefault o -> "ALTER TABLE " + qualified(o.table()) + " ALTER COLUMN " + quote(o.column()) + (o.expression() == null ? " DROP DEFAULT;" : " SET DEFAULT " + o.expression() + ";");
            case AddForeignKey o -> addForeignKey(o.table(), o.foreignKey());
            case DropForeignKey o -> "ALTER TABLE " + qualified(o.table()) + " DROP CONSTRAINT " + quote(o.foreignKey().name()) + ";";
            case AddCheck o -> "ALTER TABLE " + qualified(o.table()) + " ADD CONSTRAINT " + quote(o.check().name()) + " CHECK (" + o.check().expression() + ");";
            case DropCheck o -> "ALTER TABLE " + qualified(o.table()) + " DROP CONSTRAINT " + quote(o.check().name()) + ";";
            case CreateExtension o -> (o.extension().schema() == null ? "" : "CREATE SCHEMA IF NOT EXISTS " + quote(o.extension().schema()) + ";\n") + "CREATE EXTENSION IF NOT EXISTS " + quote(o.extension().name()) + (o.extension().schema() == null ? "" : " WITH SCHEMA " + quote(o.extension().schema())) + ";";
            case DropExtension o -> "DROP EXTENSION " + quote(o.extension().name()) + ";";
        };
    }

    private static String createTable(TableSchema table) {
        List<String> lines = new ArrayList<>();
        for (ColumnSchema column : table.columns()) lines.add("    " + column(column));
        if (!table.primaryKey().isEmpty())
            lines.add("    CONSTRAINT " + quote("pk_" + simpleName(table.name())) + " PRIMARY KEY (" + columns(table.primaryKey()) + ")");
        for (CheckConstraintSchema check : table.checks())
            lines.add("    CONSTRAINT " + quote(check.name()) + " CHECK (" + check.expression() + ")");
        StringBuilder sql = new StringBuilder();
        int dot = table.name().lastIndexOf('.');
        if (dot >= 0)
            sql.append("CREATE SCHEMA IF NOT EXISTS ").append(quote(table.name().substring(0, dot))).append(";\n");
        sql.append("CREATE TABLE ").append(qualified(table.name())).append(" (\n");
        sql.append(String.join(",\n", lines)).append("\n);");
        for (IndexSchema index : table.indexes()) sql.append("\n").append(createIndex(table.name(), index));
        return sql.toString();
    }

    private static String createIndex(String table, IndexSchema index) {
        String indexedColumns = index.columns().stream().map(c -> quote(c) + (index.operatorClass() == null ? "" : " " + index.operatorClass())).collect(Collectors.joining(", "));
        return "CREATE " + (index.unique() ? "UNIQUE " : "") + "INDEX " + quote(index.name()) + " ON " + qualified(table)
                + " USING " + index.method() + " (" + indexedColumns + ");";
    }

    private static String column(ColumnSchema column) {
        String identity = column.identity() == null ? "" : " GENERATED " + (column.identity() == IdentityGeneration.ALWAYS ? "ALWAYS" : "BY DEFAULT") + " AS IDENTITY";
        String defaultValue = column.defaultExpression() == null ? "" : " DEFAULT " + column.defaultExpression();
        return quote(column.name()) + " " + column.type() + identity + defaultValue + (column.nullable() ? "" : " NOT NULL");
    }

    private static String addForeignKey(String table, ForeignKeySchema fk) {
        return "ALTER TABLE " + qualified(table) + " ADD CONSTRAINT " + quote(fk.name()) + " FOREIGN KEY (" + columns(fk.columns()) + ") REFERENCES " + qualified(fk.referencedTable()) + " (" + columns(fk.referencedColumns()) + ")" + action("DELETE", fk.onDelete()) + action("UPDATE", fk.onUpdate()) + ";";
    }

    private static String action(String kind, ForeignKeySchema.DeleteAction action) {
        if (action == null || action == ForeignKeySchema.DeleteAction.NO_ACTION) return "";
        String value = switch (action) { case NO_ACTION -> "NO ACTION"; case RESTRICT -> "RESTRICT"; case CASCADE -> "CASCADE"; case SET_NULL -> "SET NULL"; case SET_DEFAULT -> "SET DEFAULT"; };
        return " ON " + kind + " " + value;
    }

    private static String columns(List<String> names) {
        return names.stream().map(PostgresSqlGenerator::quote).collect(Collectors.joining(", "));
    }

    static String quote(String identifier) {
        return "\"" + identifier.replace("\"", "\"\"") + "\"";
    }

    /** {@code schema.table} becomes {@code "schema"."table"}. */
    static String qualified(String storageName) {
        return schemaOf(storageName) + quote(simpleName(storageName));
    }

    private static String schemaOf(String storageName) {
        int dot = storageName.lastIndexOf('.');
        return dot < 0 ? "" : quote(storageName.substring(0, dot)) + ".";
    }

    private static String simpleName(String storageName) {
        return storageName.substring(storageName.lastIndexOf('.') + 1);
    }
}
