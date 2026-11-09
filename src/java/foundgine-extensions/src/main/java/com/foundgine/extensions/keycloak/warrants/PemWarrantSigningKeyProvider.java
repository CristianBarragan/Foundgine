package com.foundgine.extensions.keycloak.warrants;

import java.security.GeneralSecurityException;
import java.security.KeyFactory;
import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.interfaces.RSAPrivateCrtKey;
import java.security.spec.PKCS8EncodedKeySpec;
import java.security.spec.RSAPublicKeySpec;
import java.util.Base64;

public final class PemWarrantSigningKeyProvider implements IFoundgineWarrantSigningKeyProvider {
    private final String keyId;
    private final PrivateKey key;
    private final String publicPem;

    /** @param pem an unencrypted PKCS#8 RSA private key ({@code BEGIN PRIVATE KEY}) of at least 2048 bits */
    public PemWarrantSigningKeyProvider(String keyId, String pem) {
        if (keyId == null || keyId.isBlank()) throw new IllegalArgumentException("keyId is required");
        if (pem == null || pem.isBlank()) throw new IllegalArgumentException("pem is required");
        this.keyId = keyId;
        try {
            var der = Base64.getMimeDecoder().decode(pem.replaceAll("-----[A-Z ]+-----", "").trim());
            var factory = KeyFactory.getInstance("RSA");
            this.key = factory.generatePrivate(new PKCS8EncodedKeySpec(der));
            if (!(key instanceof RSAPrivateCrtKey crt)) throw new IllegalArgumentException("Warrant signing key must be an RSA CRT key.");
            if (crt.getModulus().bitLength() < 2048) throw new IllegalArgumentException("Warrant signing keys must be at least 2048 bits.");
            var pub = factory.generatePublic(new RSAPublicKeySpec(crt.getModulus(), crt.getPublicExponent()));
            this.publicPem = toPem("PUBLIC KEY", pub.getEncoded());
        } catch (GeneralSecurityException ex) {
            throw new IllegalArgumentException("Warrant signing key is not a valid PKCS#8 RSA private key.", ex);
        }
    }

    /** Development only: a new key per process. Warrants will not verify across instances or restarts. */
    public static PemWarrantSigningKeyProvider createEphemeral(String keyId) {
        try {
            var generator = KeyPairGenerator.getInstance("RSA");
            generator.initialize(2048);
            return new PemWarrantSigningKeyProvider(keyId, toPem("PRIVATE KEY", generator.generateKeyPair().getPrivate().getEncoded()));
        } catch (GeneralSecurityException ex) {
            throw new IllegalStateException("RSA is unavailable.", ex);
        }
    }

    @Override public String keyId() { return keyId; }

    @Override public PrivateKey privateKey() { return key; }

    @Override public String exportPublicKeyPem() { return publicPem; }

    static String toPem(String label, byte[] der) {
        return "-----BEGIN " + label + "-----\n"
                + Base64.getMimeEncoder(64, "\n".getBytes(java.nio.charset.StandardCharsets.US_ASCII)).encodeToString(der)
                + "\n-----END " + label + "-----\n";
    }
}
