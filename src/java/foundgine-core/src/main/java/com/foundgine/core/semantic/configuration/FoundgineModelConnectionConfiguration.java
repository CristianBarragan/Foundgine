package com.foundgine.core.semantic.configuration;

/**
 * Connects one semantic model to storage entities ({@code ConnectionMetadata}): which members
 * correspond, how values are converted, and who may follow the connection. This is the fluent
 * counterpart of the connection, conversion and authorization annotations. Relationship foreign
 * keys stay in {@link FoundgineConnectionConfiguration}.
 */
public abstract class FoundgineModelConnectionConfiguration<TModel> implements IFoundgineConfiguration {
    private final Class<?> modelType;

    /** Reads {@code TModel} from the subclass declaration. */
    protected FoundgineModelConnectionConfiguration() {
        this.modelType = TypeArguments.resolve(getClass(), FoundgineModelConnectionConfiguration.class)[0];
    }

    /** For configurations whose type argument cannot be read reflectively. */
    protected FoundgineModelConnectionConfiguration(Class<TModel> modelType) {
        this.modelType = java.util.Objects.requireNonNull(modelType, "modelType");
    }

    public abstract void configure(FoundgineModelConnectionBuilder<TModel> connections);

    @Override
    public final void apply(FoundgineConfigurationContext context) {
        configure(new FoundgineModelConnectionBuilder<>(context, modelType));
    }
}
