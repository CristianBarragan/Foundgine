package com.foundgine.extensions.keycloak.claims;

import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/** Reads Keycloak nested role claims ({@code realm_access}, {@code resource_access}). */
public final class KeycloakClaims {
    private KeycloakClaims() { }

    public static Set<String> readRoles(Map<String, Object> claims, FoundgineKeycloakOptions options) {
        var roles = new LinkedHashSet<String>();
        if (options.includeRealmRoles) addRoles(claims.get("realm_access"), null, roles);
        if (options.includeClientRoles) addRoles(claims.get("resource_access"), options.audience, roles);
        return roles;
    }

    private static void addRoles(Object node, String client, Set<String> roles) {
        // A malformed claim yields no roles (deny by default).
        if (!(node instanceof Map<?, ?> root)) return;
        Object scope = root;
        if (client != null) {
            scope = root.get(client);
            if (!(scope instanceof Map<?, ?>)) return;
        }
        if (!(((Map<?, ?>) scope).get("roles") instanceof List<?> array)) return;
        for (var r : array) if (r instanceof String role && !role.isEmpty()) roles.add(role);
    }
}
