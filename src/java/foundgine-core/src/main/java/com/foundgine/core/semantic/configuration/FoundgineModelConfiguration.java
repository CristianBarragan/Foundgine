package com.foundgine.core.semantic.configuration;

/**
 * Application/semantic configuration for one model: its semantic entity name, aliases,
 * dimensions and relationships. Says nothing about tables, columns or providers.
 */
public abstract class FoundgineModelConfiguration<TModel> implements IFoundgineConfiguration {
    private final Class<?> modelType;

    /** Reads {@code TModel} from the subclass declaration, e.g. {@code extends FoundgineModelConfiguration<Customer>}. */
    protected FoundgineModelConfiguration() {
        this.modelType = TypeArguments.resolve(getClass(), FoundgineModelConfiguration.class)[0];
    }

    /** For configurations whose type argument cannot be read reflectively. */
    protected FoundgineModelConfiguration(Class<TModel> modelType) {
        this.modelType = java.util.Objects.requireNonNull(modelType, "modelType");
    }

    public abstract void configure(FoundgineModelBuilder<TModel> model);

    @Override
    public final void apply(FoundgineConfigurationContext context) {
        configure(new FoundgineModelBuilder<>(context.addModel(modelType)));
    }
}
