package com.foundgine.core.semantic.security.warrants;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.File;
import java.math.BigDecimal;
import java.nio.file.Files;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.List;
import java.util.stream.Stream;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * Cross-language conformance: the same vectors (src/conformance/warrant-canonicalization.json) are asserted by the C#
 * test of the same name. A warrant signed by one implementation must verify in the other, so the canonical bytes and
 * digest must be identical. Override the vector directory with -Dfoundgine.conformance.dir=...
 */
class WarrantCanonicalizationConformanceTest {
    private static final ObjectMapper MAPPER = new ObjectMapper();

    private static File vectorFile() {
        String dir = System.getProperty("foundgine.conformance.dir");
        File f = new File(dir != null ? dir : "../../conformance", "warrant-canonicalization.json");
        if (!f.isFile()) throw new IllegalStateException("Conformance vectors not found: " + f.getAbsolutePath());
        return f;
    }

    static Stream<JsonNode> vectors() throws Exception {
        JsonNode root = MAPPER.readTree(Files.readAllBytes(vectorFile().toPath()));
        List<JsonNode> all = new ArrayList<>();
        root.get("vectors").forEach(all::add);
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

    private static SecurityWarrant build(JsonNode w) {
        List<CapabilityGrant> grants = new ArrayList<>();
        w.get("grants")
                .forEach(g -> grants.add(new CapabilityGrant(
                        g.get("capability").asText(),
                        g.get("operation").asText(),
                        (java.util.Collection<String>) strings(g.get("resourceScopes")))));
        JsonNode c = w.get("constraints");
        var constraints = new SecurityWarrantConstraints(
                (java.util.Collection<String>) strings(c.get("allowedTenants")),
                (java.util.Collection<String>) strings(c.get("allowedFields")),
                (java.util.Collection<String>) strings(c.get("resourceScopes")),
                (java.util.Collection<String>) strings(c.get("allowedOperations")),
                c.hasNonNull("maxResults") ? c.get("maxResults").asLong() : null,
                c.hasNonNull("maxAmount") ? new BigDecimal(c.get("maxAmount").asText()) : null);
        return new SecurityWarrant(
                w.get("id").asText(),
                w.get("issuer").asText(),
                w.get("subject").asText(),
                w.get("audience").asText(),
                grants,
                constraints,
                OffsetDateTime.parse(w.get("issuedAt").asText()).toInstant(),
                OffsetDateTime.parse(w.get("expiresAt").asText()).toInstant(),
                w.get("nonce").asText(),
                w.get("keyId").asText(),
                text(w.get("parentId")),
                new byte[0],
                text(w.get("parentDigest")),
                strings(w.get("delegationPath")));
    }

    @ParameterizedTest
    @MethodSource("vectors")
    void canonicalJsonAndDigestMatchTheReferenceVectors(JsonNode vector) {
        var warrant = build(vector.get("warrant"));
        String name = vector.get("name").asText();
        assertEquals(vector.get("unsignedJson").asText(), SecurityWarrantCanonicalizer.unsignedJson(warrant), name);
        assertEquals(vector.get("digest").asText(), SecurityWarrantCanonicalizer.digest(warrant), name);
    }

    @Test
    void unpairedSurrogatesAreRejectedRatherThanSilentlyReplaced() throws Exception {
        var w = build(vectors().findFirst().orElseThrow().get("warrant"));
        var bad = new SecurityWarrant(
                w.id(), w.issuer(), "bad\uD800", w.audience(), w.grants(), w.constraints(), w.issuedAt(),
                w.expiresAt(), w.nonce(), w.keyId(), w.parentId(), new byte[0], w.parentDigest(), w.delegationPath());
        assertThrows(IllegalArgumentException.class, () -> SecurityWarrantCanonicalizer.unsignedJson(bad));
    }

    @Test
    void instantsAreTruncatedToTheCanonicalHundredNanosecondResolution() throws Exception {
        var w = build(vectors().findFirst().orElseThrow().get("warrant"));
        var t = java.time.Instant.parse("2026-11-09T07:25:00.123456789Z");
        var precise = new SecurityWarrant(
                w.id(), w.issuer(), w.subject(), w.audience(), w.grants(), w.constraints(), t, t.plusSeconds(60),
                w.nonce(), w.keyId(), null, new byte[0], null, w.delegationPath());
        assertEquals(123456700, precise.issuedAt().getNano());
        assertEquals(123456700, precise.expiresAt().getNano());
    }
}
