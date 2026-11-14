package com.foundgine.core.semantic.metadata;

import com.foundgine.core.abstractions.EntityId;

import java.util.List;

/** Static description of a domain entity. Metadata describes what exists. */
public record EntityMetadata(
        EntityId entityId,
        String name,
        List<ColumnMetadata> columns,
        String storageName,
        List<FieldMetadata> fields,
        ColumnReference primaryKey,
        Class<?> clrType,
        boolean event,
        ColumnReference temporalColumn,
        List<AliasDeclaration> aliases,
        List<IndexMetadata> indexes,
        List<ForeignKeyMetadata> foreignKeys,
        List<CheckConstraintMetadata> checks,
        List<String> extensions) {
    public EntityMetadata(
            EntityId entityId,
            String name,
            List<ColumnMetadata> columns,
            String storageName,
            List<FieldMetadata> fields,
            ColumnReference primaryKey,
            Class<?> clrType,
            boolean event,
            ColumnReference temporalColumn,
            List<AliasDeclaration> aliases) {
        this(entityId, name, columns, storageName, fields, primaryKey, clrType, event, temporalColumn, aliases, null, null, null, null);
    }

    public EntityMetadata(EntityId entityId, String name, List<ColumnMetadata> columns) {
        this(entityId, name, columns, null, null, null, null, false, null, null, null, null, null, null);
    }

    public EntityMetadata(EntityId entityId, String name, List<ColumnMetadata> columns, String storageName, List<FieldMetadata> fields, ColumnReference primaryKey, Class<?> clrType, boolean event, ColumnReference temporalColumn, List<AliasDeclaration> aliases, List<IndexMetadata> indexes) {
        this(entityId, name, columns, storageName, fields, primaryKey, clrType, event, temporalColumn, aliases, indexes, null, null, null);
    }

    public EntityMetadata(EntityId entityId, String name, List<ColumnMetadata> columns, String storageName, List<FieldMetadata> fields, ColumnReference primaryKey, Class<?> clrType, boolean event, ColumnReference temporalColumn, List<AliasDeclaration> aliases, List<IndexMetadata> indexes, List<ForeignKeyMetadata> foreignKeys, List<CheckConstraintMetadata> checks) {
        this(entityId, name, columns, storageName, fields, primaryKey, clrType, event, temporalColumn, aliases, indexes, foreignKeys, checks, null);
    }

    public List<String> effectiveExtensions() { return extensions == null ? List.of() : List.copyOf(extensions); }

    public List<ForeignKeyMetadata> effectiveForeignKeys() { return foreignKeys == null ? List.of() : List.copyOf(foreignKeys); }
    public List<CheckConstraintMetadata> effectiveChecks() { return checks == null ? List.of() : List.copyOf(checks); }

    public List<IndexMetadata> effectiveIndexes() {
        return indexes == null ? List.of() : List.copyOf(indexes);
    }

    public String effectiveStorageName() {
        return storageName != null ? storageName : name;
    }

    public List<FieldMetadata> effectiveFields() {
        return fields != null ? fields : List.of();
    }

    public List<AliasDeclaration> effectiveAliases() {
        return aliases == null ? List.of() : List.copyOf(aliases);
    }
}
