package com.foundgine.extensions.keycloak.authentication;

import java.security.interfaces.RSAPublicKey;

/** Resolves the realm signing key for a JWT {@code kid}. Null when unknown (the token is then rejected). */
@FunctionalInterface
public interface IKeycloakKeySource {
    RSAPublicKey resolve(String keyId);

    /**
     * The issuer published by the realm's discovery document, or null when none has been discovered. Like the C#
     * JwtBearer setup, a discovered issuer (not the configured authority) is what token issuers are compared with.
     */
    default String issuer() {
        return null;
    }
}
