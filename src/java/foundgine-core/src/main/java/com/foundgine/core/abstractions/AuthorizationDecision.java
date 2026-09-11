package com.foundgine.core.abstractions;

import java.util.Objects;

/**
 * Port of {@code Foundgine.Core.Abstractions.AuthorizationDecision}.
 *
 * <p>Provider-independent authorization result. Conditional access carries a
 * predicate that must remain part of execution semantics and be evaluated
 * at the provider boundary with the current execution context.
 */
public record AuthorizationDecision(AuthorizationAccess access, AuthorizationPredicate predicate) {

    public AuthorizationDecision(AuthorizationAccess access) {
        this(access, null);
    }

    public static final AuthorizationDecision DENIED = new AuthorizationDecision(AuthorizationAccess.DENIED);
    public static final AuthorizationDecision ALLOWED = new AuthorizationDecision(AuthorizationAccess.ALLOWED);

    public static AuthorizationDecision conditional(AuthorizationPredicate predicate) {
        Objects.requireNonNull(predicate, "predicate");
        return new AuthorizationDecision(AuthorizationAccess.CONDITIONAL, predicate);
    }

    public boolean isAllowed() {
        return access == AuthorizationAccess.ALLOWED || access == AuthorizationAccess.CONDITIONAL;
    }

    public static AuthorizationDecision combine(AuthorizationDecision left, AuthorizationDecision right) {
        Objects.requireNonNull(left, "left");
        Objects.requireNonNull(right, "right");

        if (!left.isAllowed() || !right.isAllowed()) {
            return DENIED;
        }

        if (left.predicate() == null) {
            return right;
        }

        if (right.predicate() == null) {
            return left;
        }

        return conditional(AuthorizationPredicate.and(left.predicate(), right.predicate()));
    }
}
