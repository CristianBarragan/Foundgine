package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.ModelDefinition;

/** Per-entity builder for semantic configuration. See {@link FoundgineModelRegistrar} for central registration. */
public final class FoundgineModelBuilder<TModel> {
    private final ModelDefinition definition;

    FoundgineModelBuilder(ModelDefinition definition) {
        this.definition = definition;
    }

    /** Sets the semantic entity name. Defaults to the model's simple class name when omitted. */
    public FoundgineModelBuilder<TModel> toEntity(String name) {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Entity name is required.");
        definition.entityName = name.trim();
        return this;
    }

    /** Declares a historical or synonymous name for the entity. */
    public FoundgineModelBuilder<TModel> hasAlias(String name) {
        return hasAlias(name, null);
    }

    /** Declares an alias with an evidence weight (1-100). */
    public FoundgineModelBuilder<TModel> hasAlias(String name, Integer weight) {
        definition.aliases.add(ConfigurationDefinitions.alias(name, weight));
        return this;
    }

    /** Minimum alias-weight requirement (1-100) for this model's entity. */
    public FoundgineModelBuilder<TModel> hasMinimumWeight(int weight) {
        if (weight < 1 || weight > 100)
            throw new IllegalArgumentException("Weight must be between 1 and 100 (inclusive).");
        definition.minimumWeight = weight;
        return this;
    }

    /**
     * Chooses how member names become semantic field names. Defaults to {@link
     * FieldNaming#AS_DECLARED}; use {@link FieldNaming#PASCAL_CASE} when the semantic vocabulary
     * (field ids, overlays, queries) is PascalCase while the model's members are lowerCamelCase.
     */
    public FoundgineModelBuilder<TModel> hasFieldNaming(FieldNaming naming) {
        definition.fieldNaming = java.util.Objects.requireNonNull(naming, "naming");
        return this;
    }

    /** Marks the entity as an event (an occurrence in time) rather than current state. */
    public FoundgineModelBuilder<TModel> isEvent() {
        definition.event = true;
        definition.occurredAtMember = null;
        return this;
    }

    /** Marks the entity as an event and names the member holding its timestamp. */
    public FoundgineModelBuilder<TModel> isEvent(PropertyRef<TModel, ?> occurredAt) {
        definition.event = true;
        definition.occurredAtMember = PropertyReflection.resolve(occurredAt).name();
        return this;
    }

    /** Marks a member as a semantic dimension. The dimension name defaults to the member name; use {@code .as(...)}. */
    public FoundgineDimensionBuilder hasDimension(PropertyRef<TModel, ?> property) {
        String member = PropertyReflection.resolve(property).name();
        definition.dimensions.put(member, member);
        return new FoundgineDimensionBuilder(definition, member);
    }

    /** Configures semantic details of a single member, such as field aliases. */
    public FoundgineFieldBuilder property(PropertyRef<TModel, ?> property) {
        return new FoundgineFieldBuilder(definition, PropertyReflection.resolve(property).name());
    }

    /** Declares a semantic relationship through a navigation member. Continue with {@code .to(Target.class)}. */
    public FoundgineRelationshipBuilder<TModel> hasRelationship(PropertyRef<TModel, ?> navigation) {
        return new FoundgineRelationshipBuilder<>(definition, PropertyReflection.resolve(navigation));
    }

    /**
     * Declares a collection relationship by name, for models (such as records) that have no
     * navigation member to reference. The name becomes the semantic relationship name.
     */
    public <TTarget> FoundgineRelationshipTargetBuilder<TModel, TTarget> hasMany(
            String name, Class<TTarget> target) {
        return withoutNavigation(name, target, true);
    }

    /** Declares a single-valued relationship by name; see {@link #hasMany(String, Class)}. */
    public <TTarget> FoundgineRelationshipTargetBuilder<TModel, TTarget> hasOne(
            String name, Class<TTarget> target) {
        return withoutNavigation(name, target, false);
    }

    private <TTarget> FoundgineRelationshipTargetBuilder<TModel, TTarget> withoutNavigation(
            String name, Class<TTarget> target, boolean collection) {
        if (name == null || name.isBlank())
            throw new IllegalArgumentException("Relationship name is required.");
        java.util.Objects.requireNonNull(target, "target");
        String trimmed = name.trim();
        for (var existing : definition.relationships)
            if (existing.navigation.name().equals(trimmed))
                throw new IllegalStateException(
                        "Relationship "
                                + definition.modelType.getSimpleName()
                                + "."
                                + trimmed
                                + " is declared more than once.");
        MemberRef navigation =
                new MemberRef(trimmed, collection ? java.util.List.class : target, null);
        var relationship =
                new ConfigurationDefinitions.RelationshipDefinition(navigation, target);
        definition.relationships.add(relationship);
        return new FoundgineRelationshipTargetBuilder<>(relationship);
    }
}
