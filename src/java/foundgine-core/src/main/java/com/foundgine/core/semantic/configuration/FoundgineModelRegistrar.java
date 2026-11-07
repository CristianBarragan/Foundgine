package com.foundgine.core.semantic.configuration;

import java.util.Objects;

/**
 * Registers configurations on a {@link FoundgineModel}. This is the central-registration
 * counterpart of C#'s non-generic {@code FoundgineModelBuilder}; Java cannot declare a generic
 * and a non-generic class with the same name, and {@link FoundgineModelBuilder} is the
 * per-entity builder.
 */
public final class FoundgineModelRegistrar {
    private final FoundgineConfigurationContext context;

    FoundgineModelRegistrar(FoundgineConfigurationContext context) {
        this.context = context;
    }

    /** Applies a configuration class that has a no-argument constructor. */
    public FoundgineModelRegistrar configure(Class<? extends IFoundgineConfiguration> configuration) {
        Objects.requireNonNull(configuration, "configuration");
        try {
            var constructor = configuration.getDeclaredConstructor();
            constructor.setAccessible(true);
            return configure(constructor.newInstance());
        } catch (NoSuchMethodException e) {
            throw new IllegalArgumentException(
                    configuration.getName()
                            + " needs a no-argument constructor, or pass an instance to configure(...).",
                    e);
        } catch (ReflectiveOperationException e) {
            throw new IllegalStateException("Cannot create " + configuration.getName() + ".", e);
        }
    }

    /** Applies an already constructed configuration (for example one that needs constructor arguments). */
    public FoundgineModelRegistrar configure(IFoundgineConfiguration configuration) {
        Objects.requireNonNull(configuration, "configuration");
        configuration.apply(context);
        return this;
    }
}
