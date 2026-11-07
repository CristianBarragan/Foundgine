package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.*;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** Collects configuration state. Opaque to applications; created by {@link FoundgineModel}. */
public final class FoundgineConfigurationContext {
    final Map<Class<?>, ModelDefinition> models = new LinkedHashMap<>();
    final Map<Class<?>, PersistenceDefinition> persistence = new LinkedHashMap<>();
    final List<EntityMapDefinition> maps = new ArrayList<>();
    final List<ConnectionDefinition> connections = new ArrayList<>();
    final List<ModelConnectionDefinition> modelConnections = new ArrayList<>();

    FoundgineConfigurationContext() {}

    ModelDefinition addModel(Class<?> modelType) {
        if (models.containsKey(modelType))
            throw new IllegalStateException(
                    "Semantic model '" + modelType.getSimpleName() + "' is configured more than once.");
        ModelDefinition definition = new ModelDefinition(modelType);
        models.put(modelType, definition);
        return definition;
    }

    PersistenceDefinition addPersistence(Class<?> entityType) {
        if (persistence.containsKey(entityType))
            throw new IllegalStateException(
                    "Persistence model '" + entityType.getSimpleName() + "' is configured more than once.");
        PersistenceDefinition definition = new PersistenceDefinition(entityType);
        persistence.put(entityType, definition);
        return definition;
    }

    EntityMapDefinition addMap(Class<?> modelType, Class<?> entityType) {
        for (EntityMapDefinition existing : maps) {
            if (existing.modelType == modelType)
                throw new IllegalStateException(
                        "Model '" + modelType.getSimpleName() + "' already has an entity map.");
            if (existing.entityType == entityType)
                throw new IllegalStateException(
                        "Persistence model '"
                                + entityType.getSimpleName()
                                + "' is already mapped from another semantic model.");
        }
        EntityMapDefinition definition = new EntityMapDefinition(modelType, entityType);
        maps.add(definition);
        return definition;
    }

    ModelConnectionDefinition addModelConnection(Class<?> modelType, Class<?> entityType, String member) {
        ModelConnectionDefinition definition = new ModelConnectionDefinition(modelType, entityType, member);
        modelConnections.add(definition);
        return definition;
    }

    ConnectionDefinition addConnection(Class<?> sourceModel, Class<?> targetModel, String navigation) {
        ConnectionDefinition definition = new ConnectionDefinition(sourceModel, targetModel, navigation);
        connections.add(definition);
        return definition;
    }
}
