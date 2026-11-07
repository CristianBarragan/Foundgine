package com.foundgine.core.semantic.configuration;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.ModelConnectionAuthorization;
import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.ModelConnectionDefinition;
import com.foundgine.core.semantic.configuration.ConfigurationDefinitions.ModelConnectionField;
import com.foundgine.core.semantic.metadata.ConversionMetadata;
import java.util.Objects;
import java.util.function.Function;

public final class FoundgineModelConnectionEntityBuilder<TModel, TEntity> {
    private final ModelConnectionDefinition definition;
    private final Class<TEntity> entityType;

    FoundgineModelConnectionEntityBuilder(ModelConnectionDefinition definition, Class<TEntity> entityType) {
        this.definition = definition;
        this.entityType = entityType;
    }

    /** Overrides the connection name (defaults to the member name). */
    public FoundgineModelConnectionEntityBuilder<TModel, TEntity> hasName(String name) {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Connection name is required.");
        definition.name = name.trim();
        return this;
    }

    /** Pins the connection id instead of deriving it from the model and connection names. */
    public FoundgineModelConnectionEntityBuilder<TModel, TEntity> hasId(long id) {
        if (id == 0) throw new IllegalArgumentException("Connection id must be non-zero.");
        definition.explicitId = id;
        return this;
    }

    /** Maps a model member to an entity member of the same type. */
    public <TValue> FoundgineModelConnectionEntityBuilder<TModel, TEntity> maps(
            PropertyRef<TModel, TValue> source, PropertyRef<TEntity, TValue> target) {
        definition.fields.add(
                new ModelConnectionField(PropertyReflection.resolve(source), PropertyReflection.resolve(target), null, null));
        return this;
    }

    /**
     * Maps a model member to an entity member of a different type, converting the value with
     * {@code convert}. The function is recorded as {@link ConversionMetadata} for the (source,
     * target) type pair; Foundgine never calls it itself.
     */
    public <TSource, TTarget> FoundgineModelConnectionEntityBuilder<TModel, TEntity> maps(
            PropertyRef<TModel, TSource> source,
            PropertyRef<TEntity, TTarget> target,
            Function<TSource, TTarget> convert) {
        Objects.requireNonNull(convert, "convert");
        MemberRef sourceMember = PropertyReflection.resolve(source);
        MemberRef targetMember = PropertyReflection.resolve(target);
        @SuppressWarnings("unchecked")
        Class<TSource> sourceType = (Class<TSource>) sourceMember.type();
        @SuppressWarnings("unchecked")
        Class<TTarget> targetType = (Class<TTarget>) targetMember.type();
        definition.fields.add(
                new ModelConnectionField(
                        sourceMember,
                        targetMember,
                        ConversionMetadata.fromFunction(
                                boxed(sourceType), boxed(targetType), convert),
                        convert));
        return this;
    }

    /**
     * Attaches an authorization predicate to the connection, for example
     * {@code a -> a.context(User::tenantId).eq(a.resource(Customer::tenantId))}. The predicate is
     * described with {@link AuthorizationOperands} and {@link AuthorizationExpression}; only member
     * access, constants, eq, ne, and, or and not exist, the same subset as the other languages.
     */
    public <TContext> FoundgineModelConnectionEntityBuilder<TModel, TEntity> authorize(
            Class<TContext> contextType,
            String name,
            Function<AuthorizationOperands<TContext, TEntity>, AuthorizationExpression> predicate) {
        return authorize(contextType, name, null, predicate);
    }

    /** Same as {@link #authorize(Class, String, Function)} with a pinned authorization id. */
    public <TContext> FoundgineModelConnectionEntityBuilder<TModel, TEntity> authorize(
            Class<TContext> contextType,
            String name,
            Long id,
            Function<AuthorizationOperands<TContext, TEntity>, AuthorizationExpression> predicate) {
        Objects.requireNonNull(contextType, "contextType");
        Objects.requireNonNull(predicate, "predicate");
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Authorization name is required.");

        AuthorizationExpression built = predicate.apply(new AuthorizationOperands<>());
        if (built == null) throw new IllegalArgumentException("Authorization predicate returned null.");
        AuthorizationPredicate lowered = built.predicate();
        definition.authorizations.add(
                new ModelConnectionAuthorization(
                        name.trim(), id, contextType, entityType, built.toString(), lowered));
        return this;
    }

    private static Class<?> boxedRaw(Class<?> type) {
        return PropertyReflection.box(type);
    }

    @SuppressWarnings("unchecked")
    private static <T> Class<T> boxed(Class<T> type) {
        return (Class<T>) boxedRaw(type);
    }
}
