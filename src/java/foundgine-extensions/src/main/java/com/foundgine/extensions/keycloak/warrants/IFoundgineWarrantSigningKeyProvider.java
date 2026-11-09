package com.foundgine.extensions.keycloak.warrants;

import java.security.PrivateKey;

/** Source of the RSA key that signs issued warrants. Back this with a KMS/HSM in production. */
public interface IFoundgineWarrantSigningKeyProvider {
    String keyId();

    PrivateKey privateKey();

    /** Public key PEM, to configure the runtime key resolver. */
    String exportPublicKeyPem();
}
