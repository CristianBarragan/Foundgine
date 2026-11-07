package com.foundgine.core.semantic.configuration;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import java.util.Objects;

/** A node of an authorization predicate under construction. Immutable. */
public final class AuthorizationExpression {
    private final AuthorizationPredicate predicate;
    private final String text;

    AuthorizationExpression(AuthorizationPredicate predicate, String text) {
        this.predicate = predicate;
        this.text = text;
    }

    static AuthorizationExpression constantOf(Object value) {
        if (value != null
                && !(value instanceof CharSequence)
                && !(value instanceof Number)
                && !(value instanceof Boolean))
            throw new IllegalArgumentException(
                    "Authorization constants must be strings, numbers, booleans or null, not "
                            + value.getClass().getSimpleName() + ".");
        String literal = value == null ? "null" : value instanceof CharSequence ? "\"" + value + "\"" : String.valueOf(value);
        return new AuthorizationExpression(AuthorizationPredicate.constant(literal), literal);
    }

    AuthorizationPredicate predicate() {
        return predicate;
    }

    public AuthorizationExpression eq(AuthorizationExpression other) {
        return binary(AuthorizationPredicate.equal(predicate, other.predicate), "==", other);
    }

    /** Compares with a constant. */
    public AuthorizationExpression eq(Object constant) {
        return eq(constantOf(constant));
    }

    public AuthorizationExpression ne(AuthorizationExpression other) {
        return binary(AuthorizationPredicate.notEqual(predicate, other.predicate), "!=", other);
    }

    public AuthorizationExpression ne(Object constant) {
        return ne(constantOf(constant));
    }

    public AuthorizationExpression and(AuthorizationExpression other) {
        return binary(AuthorizationPredicate.and(predicate, other.predicate), "&&", other);
    }

    public AuthorizationExpression or(AuthorizationExpression other) {
        return binary(AuthorizationPredicate.or(predicate, other.predicate), "||", other);
    }

    public AuthorizationExpression not() {
        return new AuthorizationExpression(AuthorizationPredicate.not(predicate), "!" + text);
    }

    private AuthorizationExpression binary(
            AuthorizationPredicate lowered, String operator, AuthorizationExpression other) {
        Objects.requireNonNull(other, "other");
        return new AuthorizationExpression(lowered, "(" + text + " " + operator + " " + other.text + ")");
    }

    /** The predicate rendered as text, which is what {@code AuthorizationMetadata.expression()} records. */
    @Override
    public String toString() {
        return text;
    }
}
