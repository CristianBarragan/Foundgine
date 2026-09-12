package com.foundgine.runtime;

/**
 *
 * <p>Open, fluent query authoring surface entry point, provided as
 * a pair of static factories taking the receiver as an explicit first
 * parameter. {@link #typedQuery} additionally takes the entity's semantic
 * name explicitly — see {@link TypedQuery} for the design behind its
 * {@code select}/{@code include}/{@code where} surface.
 */
public final class FoundgineQuery {

    private FoundgineQuery() {
    }

    public static <T> TypedQuery<T> typedQuery(IFoundgine foundgine, String entityName) {
        return new TypedQuery<>(foundgine, entityName);
    }

    public static DynamicQuery query(IFoundgine foundgine, String entity) {
        return new DynamicQuery(foundgine, entity);
    }
}