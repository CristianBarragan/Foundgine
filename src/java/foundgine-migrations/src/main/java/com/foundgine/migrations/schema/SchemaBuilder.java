package com.foundgine.migrations.schema;

import com.foundgine.core.semantic.metadata.ColumnMetadata;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.IndexMetadata;
import com.foundgine.core.semantic.metadata.ForeignKeyMetadata;
import com.foundgine.core.semantic.metadata.CheckConstraintMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import java.math.BigDecimal;
import java.time.Instant;
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.LocalTime;
import java.time.OffsetDateTime;
import java.time.ZonedDateTime;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.UUID;

/** Derives the {@link DatabaseSchema} from the metadata the fluent model compiles to. */
public final class SchemaBuilder {
    /** PostgreSQL silently truncates longer identifiers, which would make two names collide. */
    static final int MAX_IDENTIFIER_LENGTH = 63;

    private SchemaBuilder() {}

    public static DatabaseSchema fromMetadata(MetadataRegistry registry) {
        List<TableSchema> tables = new ArrayList<>();
        Set<String> seen = new HashSet<>();
        Set<String> extensions = new java.util.TreeSet<>();
        for (EntityMetadata entity : registry.entities()) {
            if (entity.storageName() == null) continue;
            String table = entity.storageName();
            if (!seen.add(table))
                throw new IllegalStateException("Two entities map to table '" + table + "'; a table has exactly one persistence configuration.");
            tables.add(table(entity));
            extensions.addAll(entity.effectiveExtensions());
        }
        return new DatabaseSchema(tables, extensions.stream().map(PostgresExtensionSchema::new).toList());
    }

    private static TableSchema table(EntityMetadata entity) {
        String table = entity.storageName();
        checkLength("Table", table.substring(table.lastIndexOf('.') + 1));

        String keyColumn = null;
        if (entity.primaryKey() != null)
            for (ColumnMetadata column : entity.columns())
                if (column.id().equals(entity.primaryKey().columnId())) keyColumn = column.effectiveStorageName();

        List<ColumnSchema> columns = new ArrayList<>();
        for (ColumnMetadata column : entity.columns()) {
            String name = column.effectiveStorageName();
            checkLength("Column", name);
            boolean key = name.equals(keyColumn);
            Class<?> type = column.clrType();
            boolean nullable = !(column.required() || key || (type != null && type.isPrimitive()));
            IdentityGeneration identity = column.identityGeneration() == null ? null : IdentityGeneration.valueOf(column.identityGeneration());
            columns.add(new ColumnSchema(name, sqlType(table, column), nullable, column.defaultExpression(), identity));
        }

        List<IndexSchema> indexes = new ArrayList<>();
        for (IndexMetadata index : entity.effectiveIndexes()) {
            checkLength("Index", index.name());
            indexes.add(new IndexSchema(index.name(), index.columns(), index.unique(), index.method(), index.operatorClass()));
        }
        List<ForeignKeySchema> foreignKeys = entity.effectiveForeignKeys().stream()
                .map(fk -> new ForeignKeySchema(fk.name(), fk.columns(), fk.referencedTable(), fk.referencedColumns(),
                        ForeignKeySchema.DeleteAction.valueOf(fk.onDelete()), ForeignKeySchema.DeleteAction.valueOf(fk.onUpdate())))
                .toList();
        List<CheckConstraintSchema> checks = entity.effectiveChecks().stream()
                .map(c -> new CheckConstraintSchema(c.name(), c.expression())).toList();
        return new TableSchema(table, columns, keyColumn == null ? List.of() : List.of(keyColumn), indexes, foreignKeys, checks);
    }

    static String sqlType(String table, ColumnMetadata column) {
        if (column.sqlType() != null) return column.sqlType();
        Class<?> clr = column.clrType();
        String where = table + "." + column.effectiveStorageName();
        if (clr == null)
            throw new IllegalStateException(where + ": the column type is unknown; call hasColumnType(...) on the property.");
        if (clr == int.class || clr == Integer.class) return "integer";
        if (clr == long.class || clr == Long.class) return "bigint";
        if (clr == short.class || clr == Short.class) return "smallint";
        if (clr == boolean.class || clr == Boolean.class) return "boolean";
        if (clr == double.class || clr == Double.class) return "double precision";
        if (clr == float.class || clr == Float.class) return "real";
        if (clr == char.class || clr == Character.class) return "char(1)";
        if (clr == String.class) return column.maxLength() == null ? "text" : "varchar(" + column.maxLength() + ")";
        if (clr == BigDecimal.class)
            return column.precision() == null ? "numeric(18,2)" : "numeric(" + column.precision() + "," + column.scale() + ")";
        if (clr == LocalDate.class) return "date";
        if (clr == LocalTime.class) return "time";
        if (clr == LocalDateTime.class) return "timestamp";
        if (clr == OffsetDateTime.class || clr == ZonedDateTime.class || clr == Instant.class) return "timestamptz";
        if (clr == UUID.class) return "uuid";
        if (clr == byte[].class) return "bytea";
        if (clr.isEnum()) return "varchar(" + (column.maxLength() == null ? 50 : column.maxLength()) + ")";
        throw new IllegalStateException(
                where + ": no default SQL type for " + clr.getName() + "; call hasColumnType(...) on the property.");
    }

    private static void checkLength(String kind, String identifier) {
        if (identifier.length() > MAX_IDENTIFIER_LENGTH)
            throw new IllegalStateException(
                    kind + " name '" + identifier + "' is " + identifier.length()
                            + " characters; PostgreSQL allows at most " + MAX_IDENTIFIER_LENGTH + ". Give it a shorter name.");
    }
}
