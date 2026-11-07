package com.foundgine.core.semantic.configuration;

/**
 * A single unit of Foundgine configuration: a semantic model, a persistence model, an entity
 * map or a connection. Implemented by the abstract {@code Foundgine*Configuration} base
 * classes; applications extend those rather than implementing this directly.
 */
public interface IFoundgineConfiguration {
    void apply(FoundgineConfigurationContext context);
}
