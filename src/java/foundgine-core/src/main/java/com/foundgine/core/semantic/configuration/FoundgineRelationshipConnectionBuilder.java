package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.ConnectionDefinition;

public final class FoundgineRelationshipConnectionBuilder<TSource, TTarget> {
    private final ConnectionDefinition definition;

    FoundgineRelationshipConnectionBuilder(ConnectionDefinition definition) {
        this.definition = definition;
    }

    /** The storage types backing the source and target models. */
    public <TSourceEntity, TTargetEntity>
            FoundgineStorageConnectionBuilder<TSourceEntity, TTargetEntity> using(
                    Class<TSourceEntity> sourceEntity, Class<TTargetEntity> targetEntity) {
        definition.sourceEntity = java.util.Objects.requireNonNull(sourceEntity, "sourceEntity");
        definition.targetEntity = java.util.Objects.requireNonNull(targetEntity, "targetEntity");
        return new FoundgineStorageConnectionBuilder<>(definition);
    }
}
