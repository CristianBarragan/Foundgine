package com.foundgine.runtime;

/**
 * Port of {@code Foundgine.Runtime.FoundgineQueryExtensions}.
 *
 * <p>Open, fluent query authoring surface entry point. C# exposes this as
 * two extension methods ({@code foundgine.Query<T>()} and {@code
 * foundgine.Query(entity)}); Java has no extension methods, so the port is
 * a pair of static factories taking the receiver as an explicit first
 * parameter, matching this port's established convention for ported C#
 * extension methods. Java also has no reified generics, so {@link
 * #typedQuery} additionally takes the entity's semantic name explicitly —
 * see {@link TypedQuery} for the full porting decision around C#'s
 * expression-tree-based {@code Select}/{@code Include}/{@code Where}.
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
