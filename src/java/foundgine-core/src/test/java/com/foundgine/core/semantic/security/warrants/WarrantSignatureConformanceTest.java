package com.foundgine.core.semantic.security.warrants;

import static org.junit.jupiter.api.Assertions.assertEquals;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.File;
import java.math.BigDecimal;
import java.nio.file.Files;
import java.security.KeyFactory;
import java.security.PrivateKey;
import java.security.interfaces.RSAPublicKey;
import java.security.spec.PKCS8EncodedKeySpec;
import java.security.spec.X509EncodedKeySpec;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.Base64;
import java.util.Collection;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.stream.Stream;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * Signature interoperability: RSA PKCS#1 v1.5 / SHA-256 is deterministic, so signing a vector warrant must reproduce
 * the independently generated signature byte for byte, and verification must accept or reject each case with the same
 * message as C#. Vectors: src/conformance/warrant-signatures.json (test-only keys).
 * Override the directory with -Dfoundgine.conformance.dir=...
 */
class WarrantSignatureConformanceTest {
    private static final ObjectMapper MAPPER = new ObjectMapper();

    private static JsonNode root() throws Exception {
        String dir = System.getProperty("foundgine.conformance.dir");
        File f = new File(dir != null ? dir : "../../conformance", "warrant-signatures.json");
        if (!f.isFile()) throw new IllegalStateException("Conformance vectors not found: " + f.getAbsolutePath());
        return MAPPER.readTree(Files.readAllBytes(f.toPath()));
    }

    static Stream<JsonNode> cases() throws Exception {
        List<JsonNode> all = new ArrayList<>();
        root().get("cases").forEach(all::add);
        return all.stream();
    }

    private static List<String> strings(JsonNode n) {
        List<String> out = new ArrayList<>();
        if (n != null && !n.isNull()) n.forEach(x -> out.add(x.asText()));
        return out;
    }

    private static String text(JsonNode n) {
        return n == null || n.isNull() ? null : n.asText();
    }

    private static SecurityWarrant build(JsonNode w, byte[] signature) {
        List<CapabilityGrant> grants = new ArrayList<>();
        w.get("grants")
                .forEach(g -> grants.add(new CapabilityGrant(
                        g.get("capability").asText(),
                        g.get("operation").asText(),
                        (Collection<String>) strings(g.get("resourceScopes")))));
        JsonNode c = w.get("constraints");
        var constraints = new SecurityWarrantConstraints(
                (Collection<String>) strings(c.get("allowedTenants")),
                (Collection<String>) strings(c.get("allowedFields")),
                (Collection<String>) strings(c.get("resourceScopes")),
                (Collection<String>) strings(c.get("allowedOperations")),
                c.hasNonNull("maxResults") ? c.get("maxResults").asLong() : null,
                c.hasNonNull("maxAmount") ? new BigDecimal(c.get("maxAmount").asText()) : null);
        return new SecurityWarrant(
                w.get("id").asText(), w.get("issuer").asText(), w.get("subject").asText(), w.get("audience").asText(),
                grants, constraints,
                OffsetDateTime.parse(w.get("issuedAt").asText()).toInstant(),
                OffsetDateTime.parse(w.get("expiresAt").asText()).toInstant(),
                w.get("nonce").asText(), w.get("keyId").asText(), text(w.get("parentId")), signature,
                text(w.get("parentDigest")), strings(w.get("delegationPath")));
    }

    @ParameterizedTest
    @MethodSource("cases")
    void behavesLikeTheReferenceCase(JsonNode c) throws Exception {
        String name = c.get("name").asText();
        JsonNode k1 = root().get("keys").get("k-1");
        KeyFactory kf = KeyFactory.getInstance("RSA");
        PrivateKey priv = kf.generatePrivate(
                new PKCS8EncodedKeySpec(Base64.getDecoder().decode(k1.get("privateKeyPkcs8").asText())));
        RSAPublicKey pub = (RSAPublicKey) kf.generatePublic(
                new X509EncodedKeySpec(Base64.getDecoder().decode(k1.get("publicKeySpki").asText())));

        var warrant = build(c.get("warrant"), Base64.getDecoder().decode(c.get("signature").asText()));
        Set<String> resolvable = new HashSet<>(strings(c.get("resolvableKeyIds")));
        ISecurityWarrantKeyResolver resolver = id -> resolvable.contains(id) ? pub : null;

        String error = null;
        try {
            SecurityWarrantVerifier.verify(
                    warrant, resolver, OffsetDateTime.parse(c.get("now").asText()).toInstant(),
                    text(c.get("expectedIssuer")), text(c.get("expectedAudience")));
        } catch (IllegalStateException e) {
            error = e.getMessage();
        }
        assertEquals(text(c.get("error")), error, name);
        if (c.get("mustReproduceSignature").asBoolean()) {
            String mine = Base64.getEncoder()
                    .encodeToString(SecurityWarrantSigner.sign(warrant.withSignature(new byte[0]), priv).signature());
            assertEquals(c.get("signature").asText(), mine, name + " signature bytes");
        }
    }
}
