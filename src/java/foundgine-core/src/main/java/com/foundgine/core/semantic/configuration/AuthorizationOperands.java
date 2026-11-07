package com.foundgine.core.semantic.configuration;

import com.foundgine.core.abstractions.AuthorizationPredicate;

/**
 * Operands available when describing an authorization predicate: members of the caller context,
 * members of the connected entity, and constants.
 */
public final class AuthorizationOperands<TContext, TResource> {
    AuthorizationOperands() {}

    /** A member of the caller context, for example {@code a.context(User::tenantId)}. */
    public AuthorizationExpression context(PropertyRef<TContext, ?> member) {
        return new AuthorizationExpression(
                AuthorizationPredicate.member(
                        AuthorizationPredicate.contextParameter("context"), PropertyReflection.resolve(member).name()),
                "context." + PropertyReflection.resolve(member).name());
    }

    /** A member of the connected entity, for example {@code a.resource(Customer::tenantId)}. */
    public AuthorizationExpression resource(PropertyRef<TResource, ?> member) {
        return new AuthorizationExpression(
                AuthorizationPredicate.member(
                        AuthorizationPredicate.resourceParameter("resource"), PropertyReflection.resolve(member).name()),
                "resource." + PropertyReflection.resolve(member).name());
    }

    /** A constant: a string, number, boolean or null. */
    public AuthorizationExpression constant(Object value) {
        return AuthorizationExpression.constantOf(value);
    }
}
