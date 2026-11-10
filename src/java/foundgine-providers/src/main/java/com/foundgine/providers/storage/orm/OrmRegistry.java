package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;

import java.util.Collection;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/** Entity bindings and resolved relationship join keys. Immutable after creation. */
public final class OrmRegistry {
    private final Map<EntityId, OrmEntityMapping> maps;
    private final Map<RelationshipId, RelationshipMetadata> relationships;

    private OrmRegistry(
            Map<EntityId, OrmEntityMapping> maps,
            Map<RelationshipId, RelationshipMetadata> relationships) {
        this.maps = maps;
        this.relationships = relationships;
    }

    /**
     * @param entityTypes persistent class per semantic entity name; when absent the entity's
     *     {@code clrType} metadata is used.
     * @param attributeNames optional overrides {@code "Entity.Field" -> attribute}; by default the
     *     semantic field name is the attribute name.
     */
    public static OrmRegistry create(
            Collection<EntityMetadata> entities,
            Collection<RelationshipMetadata> relationships,
            Map<String, Class<?>> entityTypes,
            Map<String, String> attributeNames) {
        Objects.requireNonNull(entities, "entities");
        var maps = new LinkedHashMap<EntityId, OrmEntityMapping>();
        for (var entity : entities) {
            Class<?> type = entityTypes == null ? null : entityTypes.get(entity.name());
            if (type == null) type = entity.clrType();
            if (type == null) {
                throw new IllegalStateException(
                        "Entity '" + entity.name() + "' has no persistent class mapping.");
            }
            var bindings = new java.util.ArrayList<OrmFieldBinding>();
            for (FieldMetadata f : entity.effectiveFields()) {
                String attribute =
                        attributeNames == null
                                ? null
                                : attributeNames.get(entity.name() + "." + f.name());
                boolean pk =
                        f.column() != null
                                && entity.primaryKey() != null
                                && entity.primaryKey().columnId().equals(f.column().columnId());
                bindings.add(
                        new OrmFieldBinding(
                                f.id(),
                                attribute != null ? attribute : f.name(),
                                f.clrType(),
                                pk,
                                f.column() == null ? null : f.column().columnId()));
            }
            maps.put(
                    entity.entityId(),
                    new OrmEntityMapping(entity.entityId(), entity.name(), type, bindings));
        }
        var rels = new LinkedHashMap<RelationshipId, RelationshipMetadata>();
        if (relationships != null) for (var r : relationships) rels.put(r.id(), r);
        return new OrmRegistry(Map.copyOf(maps), Map.copyOf(rels));
    }

    public static OrmRegistry create(
            Collection<EntityMetadata> entities,
            Collection<RelationshipMetadata> relationships,
            Map<String, Class<?>> entityTypes) {
        return create(entities, relationships, entityTypes, Map.of());
    }

    public OrmEntityMapping map(EntityId id) {
        var m = maps.get(id);
        if (m == null) {
            throw new IllegalStateException("No ORM mapping is registered for entity " + id + ".");
        }
        return m;
    }

    /**
     * Join keys of {@code relationship} seen from {@code parent}. Works in both directions;
     * fails closed when keys do not resolve to mapped fields or the child is not the other end.
     */
    public OrmLink link(RelationshipId relationship, EntityId parent, EntityId child) {
        var r = relationships.get(relationship);
        if (r == null) {
            throw new IllegalStateException(
                    "Relationship traversal needs relationship metadata for "
                            + relationship
                            + ". Pass the relationships to OrmRegistry.create.");
        }
        EntityId parentKeyEntity;
        EntityId childKeyEntity;
        com.foundgine.core.abstractions.ColumnId parentColumn;
        com.foundgine.core.abstractions.ColumnId childColumn;
        if (r.source().equals(parent) && r.target().equals(child)) {
            parentKeyEntity = r.source();
            childKeyEntity = r.target();
            parentColumn = r.sourceKey().columnId();
            childColumn = r.targetKey().columnId();
        } else if (r.target().equals(parent) && r.source().equals(child)) {
            parentKeyEntity = r.target();
            childKeyEntity = r.source();
            parentColumn = r.targetKey().columnId();
            childColumn = r.sourceKey().columnId();
        } else {
            throw new IllegalStateException(
                    "Relationship " + relationship + " does not connect " + parent + " and " + child + ".");
        }
        FieldId p =
                map(parentKeyEntity)
                        .fieldForColumn(parentColumn)
                        .orElseThrow(
                                () ->
                                        new IllegalStateException(
                                                "No field of the parent maps relationship key column "
                                                        + parentColumn
                                                        + "."));
        FieldId c =
                map(childKeyEntity)
                        .fieldForColumn(childColumn)
                        .orElseThrow(
                                () ->
                                        new IllegalStateException(
                                                "No field of the child maps relationship key column "
                                                        + childColumn
                                                        + "."));
        return new OrmLink(p, c);
    }

    public List<OrmEntityMapping> all() {
        return List.copyOf(maps.values());
    }
}
