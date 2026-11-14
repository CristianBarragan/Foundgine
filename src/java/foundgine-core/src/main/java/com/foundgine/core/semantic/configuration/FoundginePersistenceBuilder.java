package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.PersistenceDefinition;
import java.util.ArrayList;
import java.util.List;

public final class FoundginePersistenceBuilder<TEntity> {
    private final PersistenceDefinition definition;

    FoundginePersistenceBuilder(PersistenceDefinition definition) {
        this.definition = definition;
    }

    /** Sets the physical table. Defaults to the storage type's simple class name when omitted. */
    public FoundginePersistenceBuilder<TEntity> toTable(String name) {
        return toTable(name, null);
    }

    /** Sets the physical table and optional schema. */
    public FoundginePersistenceBuilder<TEntity> toTable(String name, String schema) {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Table name is required.");
        if (schema != null && schema.isBlank()) throw new IllegalArgumentException("Schema must not be blank.");
        definition.table = name.trim();
        definition.schema = schema == null ? null : schema.trim();
        return this;
    }

    /** Declares the primary key. Foundgine entities have a single key column. */
    public FoundginePersistenceBuilder<TEntity> hasKey(PropertyRef<TEntity, ?> key) {
        MemberRef member = PropertyReflection.resolve(key);
        definition.key = member;
        definition.column(member);
        return this;
    }

    public FoundginePropertyBuilder property(PropertyRef<TEntity, ?> property) {
        return new FoundginePropertyBuilder(definition.column(PropertyReflection.resolve(property)));
    }

    /**
     * Declares an index over one or more members. Foundgine records it as an access-path hint on the
     * leading column, and the migration generator creates it (see {@link #hasUniqueIndex}).
     */
    @SafeVarargs
    public final FoundginePersistenceBuilder<TEntity> hasIndex(PropertyRef<TEntity, ?>... members) {
        return addIndex(null, false, members);
    }

    /** Declares a named index; migrations create it under this name. */
    @SafeVarargs
    public final FoundginePersistenceBuilder<TEntity> hasIndex(String name, PropertyRef<TEntity, ?>... members) {
        return addIndex(requireName(name), false, members);
    }

    /** Declares a unique index; migrations create it as a unique index. */
    @SafeVarargs
    public final FoundginePersistenceBuilder<TEntity> hasUniqueIndex(PropertyRef<TEntity, ?>... members) {
        return addIndex(null, true, members);
    }

    @SafeVarargs
    public final FoundginePersistenceBuilder<TEntity> hasUniqueIndex(String name, PropertyRef<TEntity, ?>... members) {
        return addIndex(requireName(name), true, members);
    }

    /** Adds a named PostgreSQL CHECK constraint to this table. Expression is trusted model-authored SQL. */
    public FoundginePersistenceBuilder<TEntity> hasCheck(String name, String expression) {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("CHECK constraint name is required.");
        if (expression == null || expression.isBlank()) throw new IllegalArgumentException("CHECK expression is required.");
        definition.checks.add(new ConfigurationDefinitions.CheckDefinition(name.trim(), expression.trim())); return this;
    }

    /** Adds a named foreign key. Table/column names are physical identifiers; actions are NO_ACTION, RESTRICT, CASCADE, SET_NULL or SET_DEFAULT. */
    @SafeVarargs
    public final FoundginePersistenceBuilder<TEntity> hasForeignKey(String name, String referencedTable, String referencedColumn, String onDelete, String onUpdate, PropertyRef<TEntity, ?>... localColumns) {
        if (referencedColumn == null || referencedColumn.isBlank()) throw new IllegalArgumentException("Referenced column is required.");
        if (localColumns == null || localColumns.length != 1) throw new IllegalArgumentException("Single-column foreign key needs exactly one local column.");
        return hasForeignKey(name, referencedTable, List.of(referencedColumn.trim()), onDelete, onUpdate, localColumns);
    }

    /** Declares a named composite or single-column foreign key. Local and referenced columns must have equal lengths. */
    @SafeVarargs
    public final FoundginePersistenceBuilder<TEntity> hasForeignKey(String name, String referencedTable, List<String> referencedColumns, String onDelete, String onUpdate, PropertyRef<TEntity, ?>... localColumns) {
        if (name == null || name.isBlank() || referencedTable == null || referencedTable.isBlank()) throw new IllegalArgumentException("Foreign-key name and referenced table are required.");
        if (referencedColumns == null || referencedColumns.isEmpty() || referencedColumns.stream().anyMatch(c -> c == null || c.isBlank())) throw new IllegalArgumentException("Referenced columns are required.");
        if (localColumns == null || localColumns.length == 0 || localColumns.length != referencedColumns.size()) throw new IllegalArgumentException("Foreign key local and referenced columns must have matching non-zero lengths.");
        List<MemberRef> locals = new ArrayList<>();
        for (PropertyRef<TEntity, ?> property : localColumns) { MemberRef local = PropertyReflection.resolve(property); definition.column(local); locals.add(local); }
        definition.foreignKeys.add(new ConfigurationDefinitions.ForeignKeyDefinition(name.trim(), List.copyOf(locals), referencedTable.trim(), referencedColumns.stream().map(String::trim).toList(), action(onDelete), action(onUpdate)));
        return this;
    }

    /** Declares an index with a PostgreSQL access method and optional operator class. */
    @SafeVarargs
    public final FoundginePersistenceBuilder<TEntity> hasIndexUsing(String name, String method, String operatorClass, PropertyRef<TEntity, ?>... members) {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Index name is required.");
        if (method == null || !method.matches("[A-Za-z][A-Za-z0-9_]*")) throw new IllegalArgumentException("Valid PostgreSQL index method is required.");
        if (operatorClass != null && !operatorClass.matches("[A-Za-z_][A-Za-z0-9_$.]*")) throw new IllegalArgumentException("Invalid PostgreSQL operator class.");
        if (members == null || members.length == 0) throw new IllegalArgumentException("An index needs at least one member.");
        List<MemberRef> list = new ArrayList<>();
        for (PropertyRef<TEntity, ?> member : members) { MemberRef resolved = PropertyReflection.resolve(member); definition.column(resolved); list.add(resolved); }
        definition.indexes.add(list); definition.indexOptions.add(new ConfigurationDefinitions.IndexOptions(name.trim(), false, method.toLowerCase(java.util.Locale.ROOT), operatorClass)); return this;
    }

    /** Declares a PostgreSQL extension required by this physical schema. */
    public FoundginePersistenceBuilder<TEntity> hasExtension(String name) {
        if (name == null || !name.matches("[A-Za-z0-9_]+")) throw new IllegalArgumentException("Invalid PostgreSQL extension name.");
        if (!definition.extensions.contains(name)) definition.extensions.add(name); return this;
    }

    private static String action(String action) {
        String value = action == null ? "NO_ACTION" : action.trim().toUpperCase(java.util.Locale.ROOT);
        if (!java.util.Set.of("NO_ACTION", "RESTRICT", "CASCADE", "SET_NULL", "SET_DEFAULT").contains(value)) throw new IllegalArgumentException("Unsupported foreign-key action: " + action);
        return value;
    }

    private static String requireName(String name) {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Index name is required.");
        return name.trim();
    }

    private FoundginePersistenceBuilder<TEntity> addIndex(
            String name, boolean unique, PropertyRef<TEntity, ?>[] members) {
        if (members == null || members.length == 0)
            throw new IllegalArgumentException("An index needs at least one member.");
        List<MemberRef> list = new ArrayList<>();
        for (PropertyRef<TEntity, ?> member : members) {
            MemberRef resolved = PropertyReflection.resolve(member);
            definition.column(resolved);
            list.add(resolved);
        }
        definition.indexes.add(list);
        definition.indexOptions.add(new ConfigurationDefinitions.IndexOptions(name, unique, "btree", null));
        return this;
    }
}
