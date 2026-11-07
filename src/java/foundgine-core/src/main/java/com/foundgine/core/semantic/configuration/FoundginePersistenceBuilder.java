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
     * leading column; it does not create the index and does not model uniqueness.
     */
    @SafeVarargs
    public final FoundginePersistenceBuilder<TEntity> hasIndex(PropertyRef<TEntity, ?>... members) {
        if (members == null || members.length == 0)
            throw new IllegalArgumentException("An index needs at least one member.");
        List<MemberRef> list = new ArrayList<>();
        for (PropertyRef<TEntity, ?> member : members) {
            MemberRef resolved = PropertyReflection.resolve(member);
            definition.column(resolved);
            list.add(resolved);
        }
        definition.indexes.add(list);
        return this;
    }
}
