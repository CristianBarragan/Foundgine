package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.ModelDefinition;
import com.foundgine.core.semantic.metadata.AliasDeclaration;
import java.util.ArrayList;

public final class FoundgineFieldBuilder {
    private final ModelDefinition definition;
    private final String member;

    FoundgineFieldBuilder(ModelDefinition definition, String member) {
        this.definition = definition;
        this.member = member;
    }

    public FoundgineFieldBuilder hasAlias(String name) {
        return hasAlias(name, null);
    }

    public FoundgineFieldBuilder hasAlias(String name, Integer weight) {
        AliasDeclaration alias = ConfigurationDefinitions.alias(name, weight);
        definition.fieldAliases.computeIfAbsent(member, k -> new ArrayList<>()).add(alias);
        return this;
    }
}
