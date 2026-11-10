package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;

import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;

/** Binding of one semantic entity to a persistent class and its attributes. */
public final class OrmEntityMapping {
    private final EntityId entityId;
    private final String name;
    private final Class<?> type;
    private final Map<FieldId, OrmFieldBinding> byId;
    private final Map<ColumnId, FieldId> byColumn = new LinkedHashMap<>();
    private final Map<String, OrmFieldBinding> byName = new LinkedHashMap<>();

    public OrmEntityMapping(
            EntityId entityId, String name, Class<?> type, List<OrmFieldBinding> fields) {
        this.entityId = Objects.requireNonNull(entityId, "entityId");
        this.name = Objects.requireNonNull(name, "name");
        this.type = Objects.requireNonNull(type, "type");
        var map = new LinkedHashMap<FieldId, OrmFieldBinding>();
        for (var f : fields) {
            if (map.put(f.id(), f) != null) {
                throw new IllegalArgumentException(
                        "Entity '" + name + "' binds field " + f.id() + " twice.");
            }
            byName.put(f.name(), f);
            if (f.column() != null) byColumn.put(f.column(), f.id());
        }
        this.byId = Collections.unmodifiableMap(map);
    }

    public EntityId entityId() {
        return entityId;
    }

    public String name() {
        return name;
    }

    public Class<?> type() {
        return type;
    }

    public java.util.Collection<OrmFieldBinding> fields() {
        return byId.values();
    }

    /** Fails closed: a field that is not bound is never readable or filterable. */
    public OrmFieldBinding field(FieldId id) {
        var b = byId.get(id);
        if (b == null) {
            throw new IllegalStateException(
                    "Entity '" + name + "' has no ORM binding for field " + id + ".");
        }
        return b;
    }

    public Optional<OrmFieldBinding> fieldByName(String attributeName) {
        return Optional.ofNullable(byName.get(attributeName));
    }

    public Optional<FieldId> fieldForColumn(ColumnId column) {
        return Optional.ofNullable(byColumn.get(column));
    }

    public List<OrmFieldBinding> primaryKeys() {
        var keys = new ArrayList<OrmFieldBinding>();
        for (var b : byId.values()) if (b.primaryKey()) keys.add(b);
        return keys;
    }
}
