package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.ConnectionDefinition;

public final class FoundgineReferenceBuilder<TPrincipal> {
    private final ConnectionDefinition definition;

    FoundgineReferenceBuilder(ConnectionDefinition definition) {
        this.definition = definition;
    }

    /** The key on the principal storage type that the foreign key points at. */
    public void references(PropertyRef<TPrincipal, ?> principalKey) {
        definition.references = PropertyReflection.resolve(principalKey);
    }
}
