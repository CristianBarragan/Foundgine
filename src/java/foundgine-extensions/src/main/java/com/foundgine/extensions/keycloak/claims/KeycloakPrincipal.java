package com.foundgine.extensions.keycloak.claims;

import java.util.Map;
import java.util.Set;

/** A validated Keycloak access token: subject, tenant (may be null) and the roles projected from its claims. */
public record KeycloakPrincipal(String subject, String preferredUsername, String tenant, Set<String> roles,
        Map<String, Object> claims) {
    public KeycloakPrincipal {
        roles = Set.copyOf(roles);
        claims = Map.copyOf(claims);
    }
}
