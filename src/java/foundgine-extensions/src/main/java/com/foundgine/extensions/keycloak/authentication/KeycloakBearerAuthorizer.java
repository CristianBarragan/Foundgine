package com.foundgine.extensions.keycloak.authentication;

import com.foundgine.extensions.keycloak.claims.KeycloakPrincipal;
import com.foundgine.providers.gateway.http.GatewayHttpAuthorizer;
import com.foundgine.providers.gateway.http.GatewayHttpRequest;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Set;

/**
 * Bearer-token authentication for the gateway HTTP host: the framework-neutral counterpart of
 * {@code AddFoundgineKeycloak} + {@code RequireAuthorization()}. The validated principal is kept per request thread for
 * {@code KeycloakSecurityExecutionContextProvider}; clear it with {@link #clear()} from the server {@code afterRequest}
 * hook. {@link #addRolePolicy} provides named policies (for example the workflow approver policy).
 */
public final class KeycloakBearerAuthorizer implements GatewayHttpAuthorizer {
    private final KeycloakJwtValidator validator;
    private final ThreadLocal<KeycloakPrincipal> current = new ThreadLocal<>();
    private final Map<String, Set<String>> policies = new LinkedHashMap<>();

    public KeycloakBearerAuthorizer(KeycloakJwtValidator validator) { this.validator = validator; }

    /** Defines a policy satisfied by any of the roles. */
    public KeycloakBearerAuthorizer addRolePolicy(String name, String... roles) {
        policies.put(name, Set.of(roles));
        return this;
    }

    @Override
    public Decision authorize(GatewayHttpRequest request) {
        current.remove();
        var header = request.header("Authorization");
        if (header == null || header.length() < 8 || !header.regionMatches(true, 0, "Bearer ", 0, 7))
            return Decision.UNAUTHENTICATED;
        try {
            current.set(validator.validate(header.substring(7).trim()));
            return Decision.ALLOWED;
        } catch (KeycloakTokenException ex) {
            return Decision.UNAUTHENTICATED;
        }
    }

    @Override
    public boolean satisfiesPolicy(GatewayHttpRequest request, String policy) {
        var principal = current.get();
        var roles = policies.get(policy);
        return principal != null && roles != null && roles.stream().anyMatch(principal.roles()::contains);
    }

    /** The principal validated for the request running on this thread, or null. */
    public KeycloakPrincipal currentPrincipal() { return current.get(); }

    public void clear() { current.remove(); }
}
