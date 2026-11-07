package com.foundgine.core.semantic.configuration;

/**
 * Connects semantic relationships (declared in model configurations) to the physical foreign key
 * that realises them. One connection configures both directions of the pair.
 */
public abstract class FoundgineConnectionConfiguration implements IFoundgineConfiguration {
    public abstract void configure(FoundgineConnectionBuilder connections);

    @Override
    public final void apply(FoundgineConfigurationContext context) {
        configure(new FoundgineConnectionBuilder(context));
    }
}
