package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.ConnectionDefinition;

public final class FoundgineStorageConnectionBuilder<TSourceEntity, TTargetEntity> {
    private final ConnectionDefinition definition;

    FoundgineStorageConnectionBuilder(ConnectionDefinition definition) {
        this.definition = definition;
    }

    /** The foreign key lives on the target storage type (the usual one-to-many shape: Customer 1 - * Order). */
    public FoundgineReferenceBuilder<TSourceEntity> hasForeignKey(PropertyRef<TTargetEntity, ?> foreignKey) {
        definition.foreignKeyOnTarget = true;
        definition.foreignKey = PropertyReflection.resolve(foreignKey);
        return new FoundgineReferenceBuilder<>(definition);
    }

    /** The foreign key lives on the source storage type (for example Order.customerId when the source is Order). */
    public FoundgineReferenceBuilder<TTargetEntity> hasForeignKeyOnSource(
            PropertyRef<TSourceEntity, ?> foreignKey) {
        definition.foreignKeyOnTarget = false;
        definition.foreignKey = PropertyReflection.resolve(foreignKey);
        return new FoundgineReferenceBuilder<>(definition);
    }
}
