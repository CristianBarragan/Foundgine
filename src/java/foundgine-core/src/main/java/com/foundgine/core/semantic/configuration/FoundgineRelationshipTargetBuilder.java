package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.RelationshipDefinition;

public final class FoundgineRelationshipTargetBuilder<TModel, TTarget> {
    private final RelationshipDefinition relationship;

    FoundgineRelationshipTargetBuilder(RelationshipDefinition relationship) {
        this.relationship = relationship;
    }

    /** The inverse navigation on the target is a collection, e.g. {@code .withMany(Customer::getOrders)}. */
    public FoundgineRelationshipTargetBuilder<TModel, TTarget> withMany(PropertyRef<TTarget, ?> inverse) {
        return withMany(inverse, null);
    }

    public FoundgineRelationshipTargetBuilder<TModel, TTarget> withMany(
            PropertyRef<TTarget, ?> inverse, String name) {
        MemberRef member = PropertyReflection.resolve(inverse);
        if (!PropertyReflection.isCollection(member.type()))
            throw new IllegalArgumentException(
                    member.name() + " is not a collection; use withOne for a single-valued inverse.");
        relationship.inverseMember = member.name();
        relationship.inverseCollection = true;
        relationship.inverseName = name;
        return this;
    }

    /** The inverse navigation on the target is a single reference. */
    public FoundgineRelationshipTargetBuilder<TModel, TTarget> withOne(PropertyRef<TTarget, ?> inverse) {
        return withOne(inverse, null);
    }

    public FoundgineRelationshipTargetBuilder<TModel, TTarget> withOne(
            PropertyRef<TTarget, ?> inverse, String name) {
        MemberRef member = PropertyReflection.resolve(inverse);
        if (PropertyReflection.isCollection(member.type()))
            throw new IllegalArgumentException(
                    member.name() + " is a collection; use withMany for a collection inverse.");
        relationship.inverseMember = member.name();
        relationship.inverseCollection = false;
        relationship.inverseName = name;
        return this;
    }

    /** Inverse collection declared by name, for target models without navigation members. */
    public FoundgineRelationshipTargetBuilder<TModel, TTarget> withMany(String inverseName) {
        return inverse(inverseName, true);
    }

    /** Inverse single reference declared by name, for target models without navigation members. */
    public FoundgineRelationshipTargetBuilder<TModel, TTarget> withOne(String inverseName) {
        return inverse(inverseName, false);
    }

    private FoundgineRelationshipTargetBuilder<TModel, TTarget> inverse(String name, boolean collection) {
        if (name == null || name.isBlank())
            throw new IllegalArgumentException("Inverse relationship name is required.");
        relationship.inverseMember = name.trim();
        relationship.inverseCollection = collection;
        relationship.inverseName = null;
        return this;
    }

    /**
     * Sets the semantic relationship name (and therefore its id). Defaults to the navigation
     * member name; {@code withMany}/{@code withOne} take an optional name for the inverse.
     */
    public FoundgineRelationshipTargetBuilder<TModel, TTarget> named(String name) {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Relationship name is required.");
        relationship.name = name.trim();
        return this;
    }

    public FoundgineRelationshipTargetBuilder<TModel, TTarget> hasAlias(String name) {
        return hasAlias(name, null);
    }

    public FoundgineRelationshipTargetBuilder<TModel, TTarget> hasAlias(String name, Integer weight) {
        relationship.aliases.add(ConfigurationDefinitions.alias(name, weight));
        return this;
    }
}
