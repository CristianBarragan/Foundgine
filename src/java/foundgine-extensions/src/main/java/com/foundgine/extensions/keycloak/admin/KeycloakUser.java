package com.foundgine.extensions.keycloak.admin;

public record KeycloakUser(String id, String username, boolean enabled, String tenantId, String provisionedBy) { }
