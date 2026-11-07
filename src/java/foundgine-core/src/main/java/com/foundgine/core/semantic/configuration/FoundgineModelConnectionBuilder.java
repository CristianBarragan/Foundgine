package com.foundgine.core.semantic.configuration;

import java.util.Objects;

public final class FoundgineModelConnectionBuilder<TModel> {
    private final FoundgineConfigurationContext context;
    private final Class<?> modelType;

    FoundgineModelConnectionBuilder(FoundgineConfigurationContext context, Class<?> modelType) {
        this.context = context;
        this.modelType = modelType;
    }

    /**
     * Declares a connection from the model member {@code member} (for example {@code
     * Order::customer}) to the storage entity {@code entity}. The connection name defaults to the
     * member name.
     */
    public <TEntity> FoundgineModelConnectionEntityBuilder<TModel, TEntity> hasConnection(
            PropertyRef<TModel, ?> member, Class<TEntity> entity) {
        String name = PropertyReflection.resolve(member).name();
        return new FoundgineModelConnectionEntityBuilder<>(
                context.addModelConnection(modelType, Objects.requireNonNull(entity, "entity"), name), entity);
    }

    /** Same as {@link #hasConnection(PropertyRef, Class)} for models without a member to point at. */
    public <TEntity> FoundgineModelConnectionEntityBuilder<TModel, TEntity> hasConnection(
            String name, Class<TEntity> entity) {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Connection name is required.");
        return new FoundgineModelConnectionEntityBuilder<>(
                context.addModelConnection(modelType, Objects.requireNonNull(entity, "entity"), name.trim()),
                entity);
    }
}
