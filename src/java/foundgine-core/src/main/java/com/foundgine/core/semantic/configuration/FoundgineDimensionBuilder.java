package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.ModelDefinition;

public final class FoundgineDimensionBuilder {
    private final ModelDefinition definition;
    private final String member;

    FoundgineDimensionBuilder(ModelDefinition definition, String member) {
        this.definition = definition;
        this.member = member;
    }

    /** Names the dimension this member represents, for example "tenant" or "country". */
    public FoundgineDimensionBuilder as(String dimension) {
        if (dimension == null || dimension.isBlank())
            throw new IllegalArgumentException("Dimension name is required.");
        definition.dimensions.put(member, dimension.trim());
        return this;
    }
}
