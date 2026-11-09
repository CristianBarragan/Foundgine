package com.foundgine.extensions.keycloak.warrants;

import com.foundgine.core.semantic.security.warrants.ISecurityWarrantKeyResolver;
import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.GeneralSecurityException;
import java.security.KeyFactory;
import java.security.interfaces.RSAPublicKey;
import java.security.spec.X509EncodedKeySpec;
import java.util.Base64;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Objects;
import java.util.Set;

/**
 * Verification side of the warrant bridge: maps a warrant key id to the RSA public key that must have signed it. Unknown
 * key ids resolve to {@code null}, which the verifier rejects. Private keys are rejected: a verifier must never hold one.
 */
public final class FoundgineWarrantKeyResolver implements ISecurityWarrantKeyResolver {
    private final Map<String, RSAPublicKey> keys = new LinkedHashMap<>();

    public FoundgineWarrantKeyResolver(Map<String, String> trustedPublicKeys) {
        trustedPublicKeys.forEach(this::add);
    }

    /** Trusts this host own signing key (under its key id) plus {@link FoundgineKeycloakOptions#trustedWarrantPublicKeys}. */
    public static FoundgineWarrantKeyResolver create(FoundgineKeycloakOptions options, IFoundgineWarrantSigningKeyProvider own) {
        Objects.requireNonNull(options, "options");
        Objects.requireNonNull(own, "own");
        var trusted = new LinkedHashMap<>(options.trustedWarrantPublicKeys);
        trusted.put(own.keyId(), own.exportPublicKeyPem());
        return new FoundgineWarrantKeyResolver(trusted);
    }

    /** Key ids this resolver trusts (never key material). */
    public Set<String> keyIds() { return Collections.unmodifiableSet(keys.keySet()); }

    private void add(String keyId, String pemOrPath) {
        if (keyId == null || keyId.isBlank()) throw new IllegalArgumentException("keyId is required");
        if (pemOrPath == null || pemOrPath.isBlank()) throw new IllegalArgumentException("pemOrPath is required");
        String pem;
        try {
            pem = pemOrPath.contains("-----BEGIN") ? pemOrPath : Files.readString(Path.of(pemOrPath));
        } catch (IOException ex) {
            throw new IllegalArgumentException("Trusted key \u0027" + keyId + "\u0027 could not be read.", ex);
        }
        if (pem.contains("PRIVATE KEY"))
            throw new SecurityException("Trusted key \u0027" + keyId + "\u0027 contains private key material; configure the public key only.");
        RSAPublicKey key;
        try {
            var der = Base64.getMimeDecoder().decode(pem.replaceAll("-----[A-Z ]+-----", "").trim());
            key = (RSAPublicKey) KeyFactory.getInstance("RSA").generatePublic(new X509EncodedKeySpec(der));
        } catch (GeneralSecurityException | IllegalArgumentException ex) {
            throw new IllegalArgumentException("Trusted key \u0027" + keyId + "\u0027 is not a valid RSA public key (SubjectPublicKeyInfo PEM).", ex);
        }
        if (key.getModulus().bitLength() < 2048) throw new SecurityException("Trusted key \u0027" + keyId + "\u0027 must be at least 2048 bits.");
        if (keys.putIfAbsent(keyId, key) != null) throw new IllegalArgumentException("Duplicate trusted warrant key id \u0027" + keyId + "\u0027.");
    }

    @Override
    public RSAPublicKey resolve(String keyId) { return keyId == null ? null : keys.get(keyId); }
}
