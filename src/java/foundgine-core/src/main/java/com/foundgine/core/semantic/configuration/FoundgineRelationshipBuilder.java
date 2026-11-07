package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.ModelDefinition;
import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.RelationshipDefinition;

public final class FoundgineRelationshipBuilder<TModel> {
    private final ModelDefinition definition;
    private final MemberRef navigation;

    FoundgineRelationshipBuilder(ModelDefinition definition, MemberRef navigation) {
        this.definition = definition;
        this.navigation = navigation;
    }

    /** The semantic model on the other end of the relationship. */
    public <TTarget> FoundgineRelationshipTargetBuilder<TModel, TTarget> to(Class<TTarget> target) {
        java.util.Objects.requireNonNull(target, "target");

        Class<?> element =
                PropertyReflection.isCollection(navigation.type())
                        ? PropertyReflection.elementType(navigation)
                        : navigation.type();
        if (element != null && !target.isAssignableFrom(element))
            throw new IllegalArgumentException(
                    definition.modelType.getSimpleName()
                            + "."
                            + navigation.name()
                            + " is a "
                            + element.getSimpleName()
                            + " navigation and cannot target "
                            + target.getSimpleName()
                            + ".");

        for (RelationshipDefinition existing : definition.relationships)
            if (existing.navigation.name().equals(navigation.name()))
                throw new IllegalStateException(
                        "Relationship "
                                + definition.modelType.getSimpleName()
                                + "."
                                + navigation.name()
                                + " is declared more than once.");

        RelationshipDefinition relationship = new RelationshipDefinition(navigation, target);
        definition.relationships.add(relationship);
        return new FoundgineRelationshipTargetBuilder<>(relationship);
    }
}
