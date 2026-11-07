package com.foundgine.core.semantic.configuration;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.ModelId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.*;
import com.foundgine.core.semantic.metadata.AliasDeclaration;
import com.foundgine.core.semantic.metadata.ColumnMetadata;
import com.foundgine.core.semantic.metadata.ColumnReference;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.ModelMetadata;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Joins the four kinds of configuration into the existing metadata records. Identities are
 * derived exactly as the annotation processor derives them (entity and field ids from semantic
 * names, column ids from storage name + column name), so metadata produced here is
 * interchangeable with generated metadata. Every problem found is reported together.
 */
final class FoundgineModelCompiler {
    private final FoundgineConfigurationContext context;
    private final List<String> errors = new ArrayList<>();
    private final Map<Class<?>, CompiledEntity> byModel = new LinkedHashMap<>();
    private final List<EntityMetadata> entities = new ArrayList<>();
    private final List<ModelMetadata> models = new ArrayList<>();
    private final List<RelationshipMetadata> relationships = new ArrayList<>();

    private FoundgineModelCompiler(FoundgineConfigurationContext context) {
        this.context = context;
    }

    static void compile(FoundgineConfigurationContext context, MetadataRegistry registry) {
        FoundgineModelCompiler compiler = new FoundgineModelCompiler(context);
        compiler.run();

        if (!compiler.errors.isEmpty()) {
            StringBuilder message = new StringBuilder("Foundgine model configuration is invalid:");
            for (String error : compiler.errors)
                message.append(System.lineSeparator()).append(" - ").append(error);
            throw new IllegalStateException(message.toString());
        }

        compiler.entities.forEach(registry::register);
        compiler.models.forEach(registry::register);
        compiler.relationships.forEach(registry::register);
    }

    private void run() {
        Map<String, List<Class<?>>> bySimpleName = new LinkedHashMap<>();
        for (Class<?> type : context.models.keySet())
            bySimpleName.computeIfAbsent(type.getSimpleName(), k -> new ArrayList<>()).add(type);
        bySimpleName.forEach(
                (name, types) -> {
                    if (types.size() > 1)
                        errors.add(
                                "Models "
                                        + join(types.stream().map(Class::getName).toList())
                                        + " share the simple name '"
                                        + name
                                        + "'; semantic model names must be unique.");
                });

        List<EntityMapDefinition> maps = validMaps();
        List<ConnectionDefinition> connections = wellFormedConnections();
        Map<Class<?>, List<MemberRef>> connectionMembers = connectionMembers(connections);

        for (EntityMapDefinition map : maps) compileEntity(map, connectionMembers);

        for (ModelDefinition model : context.models.values()) {
            CompiledEntity compiled = byModel.get(model.modelType);
            this.models.add(
                    new ModelMetadata(
                            ModelId.create(model.modelType.getSimpleName()),
                            model.modelType.getSimpleName(),
                            compiled == null ? null : compiled.id,
                            model.minimumWeight));
        }

        List<ConnectionDefinition> checked = new ArrayList<>();
        for (ConnectionDefinition connection : connections)
            if (connectionMatchesMaps(connection)) checked.add(connection);
        compileRelationships(checked);
    }

    // ---- maps -------------------------------------------------------------------------

    private List<EntityMapDefinition> validMaps() {
        List<EntityMapDefinition> valid = new ArrayList<>();
        for (EntityMapDefinition map : context.maps) {
            String label = map.modelType.getSimpleName() + " -> " + map.entityType.getSimpleName();
            if (!context.models.containsKey(map.modelType)) {
                errors.add(
                        "Entity map "
                                + label
                                + ": "
                                + map.modelType.getSimpleName()
                                + " has no FoundgineModelConfiguration.");
                continue;
            }
            if (!context.persistence.containsKey(map.entityType)) {
                errors.add(
                        "Entity map "
                                + label
                                + ": "
                                + map.entityType.getSimpleName()
                                + " has no FoundginePersistenceConfiguration.");
                continue;
            }
            valid.add(map);
        }

        Map<String, List<EntityMapDefinition>> byEntityName = new LinkedHashMap<>();
        for (EntityMapDefinition map : valid)
            byEntityName
                    .computeIfAbsent(entityNameOf(context.models.get(map.modelType)), k -> new ArrayList<>())
                    .add(map);
        byEntityName.forEach(
                (name, group) -> {
                    if (group.size() > 1)
                        errors.add(
                                "Models "
                                        + join(group.stream().map(m -> m.modelType.getSimpleName()).toList())
                                        + " share the semantic entity name '"
                                        + name
                                        + "'.");
                });
        return valid;
    }

    private static String entityNameOf(ModelDefinition model) {
        return model.entityName != null ? model.entityName : model.modelType.getSimpleName();
    }

    private static String storageNameOf(PersistenceDefinition persistence) {
        String table = persistence.table != null ? persistence.table : persistence.entityType.getSimpleName();
        return persistence.schema == null ? table : persistence.schema + "." + table;
    }

    // ---- connections ------------------------------------------------------------------

    private List<ConnectionDefinition> wellFormedConnections() {
        List<ConnectionDefinition> result = new ArrayList<>();
        for (ConnectionDefinition connection : context.connections) {
            String name = connectionName(connection);
            if (connection.sourceEntity == null || connection.targetEntity == null) {
                errors.add("Connection " + name + ": call using(SourceEntity.class, TargetEntity.class).");
                continue;
            }
            if (connection.foreignKey == null || connection.references == null) {
                errors.add("Connection " + name + ": call hasForeignKey(...).references(...).");
                continue;
            }
            result.add(connection);
        }
        return result;
    }

    private static String connectionName(ConnectionDefinition connection) {
        return connection.sourceModel.getSimpleName()
                + " -> "
                + connection.targetModel.getSimpleName()
                + (connection.navigation == null ? "" : " (" + connection.navigation + ")");
    }

    /** Storage members that connections use as keys; they become columns even when nothing else mentions them. */
    private static Map<Class<?>, List<MemberRef>> connectionMembers(List<ConnectionDefinition> connections) {
        Map<Class<?>, List<MemberRef>> result = new LinkedHashMap<>();
        for (ConnectionDefinition connection : connections) {
            Class<?> foreignKeyOwner =
                    connection.foreignKeyOnTarget ? connection.targetEntity : connection.sourceEntity;
            Class<?> principalOwner =
                    connection.foreignKeyOnTarget ? connection.sourceEntity : connection.targetEntity;
            result.computeIfAbsent(foreignKeyOwner, k -> new ArrayList<>()).add(connection.foreignKey);
            result.computeIfAbsent(principalOwner, k -> new ArrayList<>()).add(connection.references);
        }
        return result;
    }

    private boolean connectionMatchesMaps(ConnectionDefinition connection) {
        String name = connectionName(connection);
        CompiledEntity source = byModel.get(connection.sourceModel);
        CompiledEntity target = byModel.get(connection.targetModel);
        if (source == null || target == null) {
            errors.add("Connection " + name + ": both models need a model configuration and an entity map.");
            return false;
        }

        if (source.entityType != connection.sourceEntity || target.entityType != connection.targetEntity) {
            errors.add(
                    "Connection "
                            + name
                            + ": using("
                            + connection.sourceEntity.getSimpleName()
                            + ", "
                            + connection.targetEntity.getSimpleName()
                            + ") does not match the entity maps ("
                            + source.entityType.getSimpleName()
                            + ", "
                            + target.entityType.getSimpleName()
                            + ").");
            return false;
        }

        MemberRef foreignKey = connection.foreignKey;
        MemberRef principal = connection.references;
        if (PropertyReflection.box(foreignKey.type()) != PropertyReflection.box(principal.type())) {
            errors.add(
                    "Connection "
                            + name
                            + ": foreign key "
                            + foreignKey.name()
                            + " ("
                            + foreignKey.type().getSimpleName()
                            + ") and referenced key "
                            + principal.name()
                            + " ("
                            + principal.type().getSimpleName()
                            + ") must have the same type.");
            return false;
        }
        return true;
    }

    // ---- entities ---------------------------------------------------------------------

    private void compileEntity(EntityMapDefinition map, Map<Class<?>, List<MemberRef>> connectionMembers) {
        ModelDefinition model = context.models.get(map.modelType);
        PersistenceDefinition persistence = context.persistence.get(map.entityType);
        String entityName = entityNameOf(model);
        EntityId entityId = EntityId.create(entityName);
        String storageName = storageNameOf(persistence);
        String label = map.modelType.getSimpleName() + " -> " + map.entityType.getSimpleName();
        String entityLabel = map.entityType.getSimpleName();

        // Columns come from the persistence configuration plus anything the map or a connection
        // refers to. The persistence definition itself is never mutated.
        List<ColumnDefinition> columns = new ArrayList<>(persistence.columns);
        for (MemberPair pair : map.properties) ensureColumn(columns, pair.entity());
        for (MemberRef member : connectionMembers.getOrDefault(map.entityType, List.of()))
            ensureColumn(columns, member);

        Map<String, List<String>> byColumnName = new LinkedHashMap<>();
        for (ColumnDefinition column : columns)
            byColumnName
                    .computeIfAbsent(column.effectiveColumnName(), k -> new ArrayList<>())
                    .add(column.member.name());
        byColumnName.forEach(
                (columnName, members) -> {
                    if (members.size() > 1)
                        errors.add(
                                entityLabel
                                        + ": members "
                                        + join(members)
                                        + " share the column name '"
                                        + columnName
                                        + "'.");
                });

        CompiledEntity compiled = new CompiledEntity(map.entityType, entityName, entityId);
        List<ColumnMetadata> columnMetadata = new ArrayList<>();
        for (ColumnDefinition column : columns) {
            ColumnId columnId = ColumnId.create(storageName, column.effectiveColumnName());
            compiled.columns.put(column.member.name(), columnId);
            columnMetadata.add(
                    new ColumnMetadata(
                            columnId, column.effectiveColumnName(), null, column.maxLength, column.required));
        }

        Set<String> indexedMembers = new HashSet<>();
        for (List<MemberRef> index : persistence.indexes) indexedMembers.add(index.get(0).name());

        List<FieldMetadata> fields = new ArrayList<>();
        Map<String, FieldMetadata> fieldByMember = new LinkedHashMap<>();
        for (MemberPair pair : map.properties) {
            String dimension = model.dimensions.get(pair.model().name());
            List<AliasDeclaration> aliases = model.fieldAliases.get(pair.model().name());
            String semanticName = model.fieldNaming.apply(pair.model().name());
            FieldMetadata fieldMetadata =
                    new FieldMetadata(
                            FieldId.create(entityName, semanticName),
                            semanticName,
                            pair.model().type(),
                            new ColumnReference(entityId, compiled.columns.get(pair.entity().name())),
                            dimension,
                            indexedMembers.contains(pair.entity().name()),
                            aliases == null || aliases.isEmpty() ? null : List.copyOf(aliases));
            fields.add(fieldMetadata);
            fieldByMember.put(pair.model().name(), fieldMetadata);
        }

        Set<String> mappedMembers = new HashSet<>();
        for (MemberPair pair : map.properties) mappedMembers.add(pair.model().name());
        for (String member : model.dimensions.keySet())
            if (!mappedMembers.contains(member))
                errors.add(label + ": dimension '" + member + "' refers to a member that is not mapped.");
        for (String member : model.fieldAliases.keySet())
            if (!mappedMembers.contains(member))
                errors.add(label + ": aliases on '" + member + "' refer to a member that is not mapped.");

        ColumnReference primaryKey = null;
        if (persistence.key == null) {
            errors.add(entityLabel + ": no key declared; call hasKey(Entity::getId).");
        } else {
            ColumnId keyColumn = compiled.columns.get(persistence.key.name());
            primaryKey = new ColumnReference(entityId, keyColumn);
            boolean mapped =
                    fields.stream().anyMatch(f -> f.column() != null && f.column().columnId().equals(keyColumn));
            if (!mapped)
                errors.add(
                        label
                                + ": key member '"
                                + persistence.key.name()
                                + "' is not mapped; map the model member that holds it.");
        }

        ColumnReference temporalColumn = null;
        if (model.event && model.occurredAtMember != null) {
            FieldMetadata field = fieldByMember.get(model.occurredAtMember);
            if (field == null)
                errors.add(label + ": event timestamp '" + model.occurredAtMember + "' is not mapped.");
            else temporalColumn = field.column();
        }

        byModel.put(map.modelType, compiled);
        entities.add(
                new EntityMetadata(
                        entityId,
                        entityName,
                        columnMetadata,
                        storageName,
                        fields,
                        primaryKey,
                        map.modelType,
                        model.event,
                        temporalColumn,
                        model.aliases.isEmpty() ? null : List.copyOf(model.aliases)));
    }

    private static void ensureColumn(List<ColumnDefinition> columns, MemberRef member) {
        for (ColumnDefinition existing : columns)
            if (existing.member.name().equals(member.name())) return;
        columns.add(new ColumnDefinition(member));
    }

    // ---- relationships ----------------------------------------------------------------

    private void compileRelationships(List<ConnectionDefinition> connections) {
        List<ResolvedRelationship> resolved = new ArrayList<>();

        for (ModelDefinition model : context.models.values())
            for (RelationshipDefinition declared : model.relationships)
                resolved.add(
                        new ResolvedRelationship(
                                model.modelType,
                                declared.targetModelType,
                                declared.navigation.name(),
                                declared.name != null ? declared.name : declared.navigation.name(),
                                declared.inverseMember,
                                PropertyReflection.isCollection(declared.navigation.type()),
                                List.copyOf(declared.aliases)));

        // A relationship that names its inverse also defines the inverse navigation, unless the
        // target model declared that navigation itself (an explicit declaration wins).
        for (ModelDefinition model : context.models.values())
            for (RelationshipDefinition declared : model.relationships) {
                if (declared.inverseMember == null) continue;
                boolean exists = false;
                for (ResolvedRelationship r : resolved)
                    if (r.sourceModel == declared.targetModelType && r.member.equals(declared.inverseMember))
                        exists = true;
                if (exists) continue;

                resolved.add(
                        new ResolvedRelationship(
                                declared.targetModelType,
                                model.modelType,
                                declared.inverseMember,
                                declared.inverseName != null ? declared.inverseName : declared.inverseMember,
                                declared.navigation.name(),
                                declared.inverseCollection,
                                List.of()));
            }

        Set<ConnectionDefinition> used = new HashSet<>();

        for (ResolvedRelationship relationship : resolved) {
            String label = relationship.sourceModel.getSimpleName() + "." + relationship.member;
            CompiledEntity source = byModel.get(relationship.sourceModel);
            if (source == null) {
                errors.add(
                        "Relationship "
                                + label
                                + ": "
                                + relationship.sourceModel.getSimpleName()
                                + " has no entity map.");
                continue;
            }
            CompiledEntity target = byModel.get(relationship.targetModel);
            if (target == null) {
                errors.add(
                        "Relationship "
                                + label
                                + ": target "
                                + relationship.targetModel.getSimpleName()
                                + " has no entity map.");
                continue;
            }

            List<ConnectionDefinition> matched = new ArrayList<>();
            List<Direction> directions = new ArrayList<>();
            boolean ambiguous = false;
            for (ConnectionDefinition connection : connections) {
                Direction direction = match(connection, relationship);
                if (direction == Direction.AMBIGUOUS) ambiguous = true;
                else if (direction != Direction.NONE) {
                    matched.add(connection);
                    directions.add(direction);
                }
            }

            if (ambiguous) {
                errors.add(
                        "Relationship "
                                + label
                                + ": a connection with the same model on both ends cannot tell the directions"
                                + " apart; pass the navigation, e.g. hasRelationship("
                                + relationship.sourceModel.getSimpleName()
                                + ".class, "
                                + relationship.targetModel.getSimpleName()
                                + ".class, "
                                + relationship.sourceModel.getSimpleName()
                                + "::get...).");
                continue;
            }
            if (matched.isEmpty()) {
                errors.add(
                        "Relationship "
                                + label
                                + ": no connection configured. Add connections.hasRelationship("
                                + relationship.sourceModel.getSimpleName()
                                + ".class, "
                                + relationship.targetModel.getSimpleName()
                                + ".class).using(...).hasForeignKey(...).references(...).");
                continue;
            }
            if (matched.size() > 1) {
                errors.add(
                        "Relationship "
                                + label
                                + ": matched by several connections ("
                                + String.join("; ", matched.stream().map(FoundgineModelCompiler::connectionName).toList())
                                + "); pass a navigation to tell them apart.");
                continue;
            }

            ConnectionDefinition connection = matched.get(0);
            Direction direction = directions.get(0);
            used.add(connection);

            CompiledEntity connectionSource = byModel.get(connection.sourceModel);
            CompiledEntity connectionTarget = byModel.get(connection.targetModel);

            ColumnReference foreignKeyRef =
                    keyRef(connection.foreignKeyOnTarget ? connectionTarget : connectionSource, connection.foreignKey);
            ColumnReference principalRef =
                    keyRef(connection.foreignKeyOnTarget ? connectionSource : connectionTarget, connection.references);

            // Keys always describe each side of the semantic relationship, whichever side
            // physically owns the foreign key.
            ColumnReference connectionSourceKey = connection.foreignKeyOnTarget ? principalRef : foreignKeyRef;
            ColumnReference connectionTargetKey = connection.foreignKeyOnTarget ? foreignKeyRef : principalRef;
            ColumnReference sourceKey = direction == Direction.FORWARD ? connectionSourceKey : connectionTargetKey;
            ColumnReference targetKey = direction == Direction.FORWARD ? connectionTargetKey : connectionSourceKey;

            this.relationships.add(
                    new RelationshipMetadata(
                            RelationshipId.create(source.name, relationship.name),
                            source.id,
                            target.id,
                            relationship.name,
                            sourceKey,
                            targetKey,
                            relationship.collection,
                            relationship.aliases.isEmpty() ? null : relationship.aliases));
        }

        for (ConnectionDefinition connection : connections)
            if (!used.contains(connection))
                errors.add("Connection " + connectionName(connection) + " does not match any semantic relationship.");
    }

    private static ColumnReference keyRef(CompiledEntity entity, MemberRef member) {
        return new ColumnReference(entity.id, entity.columns.get(member.name()));
    }

    private static Direction match(ConnectionDefinition connection, ResolvedRelationship relationship) {
        boolean forward =
                relationship.sourceModel == connection.sourceModel
                        && relationship.targetModel == connection.targetModel
                        && (connection.navigation == null || connection.navigation.equals(relationship.member));

        boolean reverse =
                relationship.sourceModel == connection.targetModel
                        && relationship.targetModel == connection.sourceModel
                        && (connection.navigation == null
                                || connection.navigation.equals(relationship.oppositeMember));

        if (forward && reverse) return Direction.AMBIGUOUS;
        if (forward) return Direction.FORWARD;
        if (reverse) return Direction.REVERSE;
        return Direction.NONE;
    }

    private static String join(List<String> values) {
        return String.join(", ", values);
    }

    private enum Direction {
        NONE,
        FORWARD,
        REVERSE,
        AMBIGUOUS
    }

    private record ResolvedRelationship(
            Class<?> sourceModel,
            Class<?> targetModel,
            String member,
            String name,
            String oppositeMember,
            boolean collection,
            List<AliasDeclaration> aliases) {}

    private static final class CompiledEntity {
        final Class<?> entityType;
        final String name;
        final EntityId id;
        /** Storage member name to physical column id. */
        final Map<String, ColumnId> columns = new LinkedHashMap<>();

        CompiledEntity(Class<?> entityType, String name, EntityId id) {
            this.entityType = entityType;
            this.name = name;
            this.id = id;
        }
    }
}
