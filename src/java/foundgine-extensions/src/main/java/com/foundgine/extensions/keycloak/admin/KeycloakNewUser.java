package com.foundgine.extensions.keycloak.admin;

import java.util.List;

public record KeycloakNewUser(String username, String email, String firstName, String lastName, List<String> requiredActions) {
    public KeycloakNewUser {
        requiredActions = List.copyOf(requiredActions == null ? List.of() : requiredActions);
    }
}
