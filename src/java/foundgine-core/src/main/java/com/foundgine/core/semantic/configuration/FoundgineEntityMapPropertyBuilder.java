package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.EntityMapDefinition;
import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.MemberPair;

public final class FoundgineEntityMapPropertyBuilder<TModel, TEntity, TProperty> {
    private final EntityMapDefinition definition;
    private final MemberRef model;

    FoundgineEntityMapPropertyBuilder(EntityMapDefinition definition, MemberRef model) {
        this.definition = definition;
        this.model = model;
    }

    /** The storage member that holds this value. Both members must have the same type. */
    public FoundgineEntityMapBuilder<TModel, TEntity> to(PropertyRef<TEntity, TProperty> entityProperty) {
        MemberRef entity = PropertyReflection.resolve(entityProperty);
        for (MemberPair existing : definition.properties)
            if (existing.model().name().equals(model.name()))
                throw new IllegalStateException(
                        definition.modelType.getSimpleName() + "." + model.name() + " is mapped more than once.");
        definition.properties.add(new MemberPair(model, entity));
        return new FoundgineEntityMapBuilder<>(definition);
    }
}
