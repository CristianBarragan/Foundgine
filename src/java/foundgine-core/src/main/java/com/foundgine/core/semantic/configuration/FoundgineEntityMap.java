package com.foundgine.core.semantic.configuration;

/**
 * Maps a semantic model onto its persistence type, member by member. The mapping lives outside
 * both types so neither needs to reference the other.
 */
public abstract class FoundgineEntityMap<TModel, TEntity> implements IFoundgineConfiguration {
    private final Class<?> modelType;
    private final Class<?> entityType;

    protected FoundgineEntityMap() {
        Class<?>[] arguments = TypeArguments.resolve(getClass(), FoundgineEntityMap.class);
        this.modelType = arguments[0];
        this.entityType = arguments[1];
    }

    protected FoundgineEntityMap(Class<TModel> modelType, Class<TEntity> entityType) {
        this.modelType = java.util.Objects.requireNonNull(modelType, "modelType");
        this.entityType = java.util.Objects.requireNonNull(entityType, "entityType");
    }

    public abstract void configure(FoundgineEntityMapBuilder<TModel, TEntity> map);

    @Override
    public final void apply(FoundgineConfigurationContext context) {
        configure(new FoundgineEntityMapBuilder<>(context.addMap(modelType, entityType)));
    }
}
