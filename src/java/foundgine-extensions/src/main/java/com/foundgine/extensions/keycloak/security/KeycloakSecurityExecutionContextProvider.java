package com.foundgine.extensions.keycloak.security;

import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.extensions.keycloak.claims.KeycloakPrincipal;
import com.foundgine.extensions.keycloak.warrants.IFoundgineWarrantIssuer;
import java.util.function.Supplier;

/**
 * Host-side {@link ISecurityExecutionContextProvider}: the validated Keycloak access token is the only input. Subject,
 * tenant and roles come from the validated principal, never from the request body or headers. Returns null (so callers
 * fail as unauthenticated) when the token has no subject, no tenant, or no mapped grants.
 *
 * <p>A fresh warrant is minted on every call: the runtime consumes each warrant nonce once (replay protection), so reusing
 * one warrant for a second execution in the same request would be rejected.
 */
public final class KeycloakSecurityExecutionContextProvider implements ISecurityExecutionContextProvider {
    private final Supplier<KeycloakPrincipal> principal;
    private final IFoundgineWarrantIssuer issuer;

    public KeycloakSecurityExecutionContextProvider(Supplier<KeycloakPrincipal> principal, IFoundgineWarrantIssuer issuer) {
        this.principal = principal;
        this.issuer = issuer;
    }

    @Override
    public SecurityExecutionContext getSecurityExecutionContext() {
        var p = principal.get();
        if (p == null || p.subject() == null || p.subject().isBlank() || p.tenant() == null || p.tenant().isBlank()) return null;
        return issuer.issue(p.subject(), p.tenant(), p.roles());
    }
}
