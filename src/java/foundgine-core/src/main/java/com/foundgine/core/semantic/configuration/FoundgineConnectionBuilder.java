package com.foundgine.core.semantic.configuration;

public final class FoundgineConnectionBuilder {
    private final FoundgineConfigurationContext context;

    FoundgineConnectionBuilder(FoundgineConfigurationContext context) {
        this.context = context;
    }

    /** Configures the relationship between two semantic models. */
    public <TSource, TTarget> FoundgineRelationshipConnectionBuilder<TSource, TTarget> hasRelationship(
            Class<TSource> source, Class<TTarget> target) {
        return new FoundgineRelationshipConnectionBuilder<>(context.addConnection(source, target, null));
    }

    /**
     * Configures one relationship when the same two models are related more than once (for example
     * billing and shipping customer): {@code navigation} is the member on {@code source} that
     * identifies which one this is.
     */
    public <TSource, TTarget> FoundgineRelationshipConnectionBuilder<TSource, TTarget> hasRelationship(
            Class<TSource> source, Class<TTarget> target, PropertyRef<TSource, ?> navigation) {
        String name = PropertyReflection.resolve(navigation).name();
        return new FoundgineRelationshipConnectionBuilder<>(context.addConnection(source, target, name));
    }

    /**
     * Same as {@link #hasRelationship(Class, Class, PropertyRef)} for models without navigation
     * members: {@code navigation} is the relationship name declared with {@code hasMany(name, ...)}
     * or {@code hasOne(name, ...)}.
     */
    public <TSource, TTarget> FoundgineRelationshipConnectionBuilder<TSource, TTarget> hasRelationship(
            Class<TSource> source, Class<TTarget> target, String navigation) {
        if (navigation == null || navigation.isBlank())
            throw new IllegalArgumentException("Navigation name is required.");
        return new FoundgineRelationshipConnectionBuilder<>(
                context.addConnection(source, target, navigation.trim()));
    }
}
