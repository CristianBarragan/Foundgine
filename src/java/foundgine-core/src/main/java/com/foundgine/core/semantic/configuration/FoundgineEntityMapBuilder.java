package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.EntityMapDefinition;

public final class FoundgineEntityMapBuilder<TModel, TEntity> {
    private final EntityMapDefinition definition;

    FoundgineEntityMapBuilder(EntityMapDefinition definition) {
        this.definition = definition;
    }

    /** Selects a model member to map. Complete the mapping with {@code .to(Storage::getMember)}. */
    public <TProperty> FoundgineEntityMapPropertyBuilder<TModel, TEntity, TProperty> property(
            PropertyRef<TModel, TProperty> modelProperty) {
        return new FoundgineEntityMapPropertyBuilder<>(definition, PropertyReflection.resolve(modelProperty));
    }
}
