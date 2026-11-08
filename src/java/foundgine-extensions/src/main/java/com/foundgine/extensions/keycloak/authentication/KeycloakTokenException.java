package com.foundgine.extensions.keycloak.authentication;

/** The bearer token failed validation. The message never contains the token. */
public final class KeycloakTokenException extends Exception {
    private static final long serialVersionUID = 1L;

    public KeycloakTokenException(String message) { super(message); }
}
