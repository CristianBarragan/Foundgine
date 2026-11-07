package com.foundgine.core.semantic.configuration;

/**
 * Provider-independent persistence configuration for one storage type: table, columns, key and
 * indexes. Execution integrations (SQL, JPA, ...) translate this metadata; it carries no
 * dependency on any of them.
 */
public abstract class FoundginePersistenceConfiguration<TEntity> implements IFoundgineConfiguration {
    private final Class<?> entityType;

    protected FoundginePersistenceConfiguration() {
        this.entityType = TypeArguments.resolve(getClass(), FoundginePersistenceConfiguration.class)[0];
    }

    protected FoundginePersistenceConfiguration(Class<TEntity> entityType) {
        this.entityType = java.util.Objects.requireNonNull(entityType, "entityType");
    }

    public abstract void configure(FoundginePersistenceBuilder<TEntity> model);

    @Override
    public final void apply(FoundgineConfigurationContext context) {
        configure(new FoundginePersistenceBuilder<>(context.addPersistence(entityType)));
    }
}
