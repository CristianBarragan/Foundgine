package com.foundgine.core.semantic.configuration;

/**
 * How a model member name becomes the semantic field name (and therefore the field id). Members
 * are always referred to by their declared name inside configuration; this only controls the name
 * that queries, aliases and field ids see.
 */
public enum FieldNaming {
    /** The member name exactly as declared, e.g. {@code name} or {@code customerId}. */
    AS_DECLARED,

    /** The member name with its first letter upper-cased, e.g. {@code Name} or {@code CustomerId}. */
    PASCAL_CASE;

    String apply(String member) {
        if (this == PASCAL_CASE && !member.isEmpty())
            return Character.toUpperCase(member.charAt(0)) + member.substring(1);
        return member;
    }
}
