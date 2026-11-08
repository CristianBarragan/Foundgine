package com.foundgine.extensions.keycloak.configuration;

import java.util.ArrayList;
import java.util.List;

/** One Foundgine capability grant given to holders of a Keycloak role. */
public final class FoundgineKeycloakGrant {
    public String capability = "";
    public String operation = "";
    public List<String> resourceScopes = new ArrayList<>();

    public FoundgineKeycloakGrant() { }

    public FoundgineKeycloakGrant(String capability, String operation, String... resourceScopes) {
        this.capability = capability;
        this.operation = operation;
        this.resourceScopes = new ArrayList<>(List.of(resourceScopes));
    }
}
