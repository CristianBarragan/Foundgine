package com.foundgine.providers.gateway.approvals;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.File;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.HexFormat;
import java.util.List;
import java.util.stream.Stream;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * Cross-language conformance for the approval payload envelope: the same vectors
 * (src/conformance/approval-envelope-vectors.json) are asserted by the C# test of the same name. Override the vector
 * directory with -Dfoundgine.conformance.dir=...
 */
class ApprovalPayloadEnvelopeConformanceTest {
    private static final ObjectMapper MAPPER = new ObjectMapper();

    private static JsonNode root() throws Exception {
        String dir = System.getProperty("foundgine.conformance.dir");
        File f = new File(dir != null ? dir : "../../conformance", "approval-envelope-vectors.json");
        if (!f.isFile()) throw new IllegalStateException("Conformance vectors not found: " + f.getAbsolutePath());
        return MAPPER.readTree(Files.readAllBytes(f.toPath()));
    }

    private static FoundgineGatewayApprovalKeyProvider keys(JsonNode root) {
        JsonNode k = root.get("keys");
        String current = root.get("currentKeyId").asText();
        return new FoundgineGatewayApprovalKeyProvider() {
            @Override public String currentKeyId() { return current; }
            @Override public byte[] key(String keyId) {
                JsonNode hex = k.get(keyId);
                if (hex == null) throw new IllegalStateException("Unknown or retired approval key: " + keyId);
                return HexFormat.of().parseHex(hex.asText());
            }
        };
    }

    static Stream<JsonNode> sealCases() throws Exception {
        List<JsonNode> all = new ArrayList<>(); root().get("sealCases").forEach(all::add); return all.stream();
    }

    static Stream<JsonNode> acceptCases() throws Exception {
        List<JsonNode> all = new ArrayList<>(); root().get("acceptCases").forEach(all::add); return all.stream();
    }

    static Stream<JsonNode> rejectCases() throws Exception {
        List<JsonNode> all = new ArrayList<>(); root().get("rejectCases").forEach(all::add); return all.stream();
    }

    @ParameterizedTest @MethodSource("sealCases")
    void seal_reproducesReferenceEnvelope_andOpensBack(JsonNode c) throws Exception {
        JsonNode root = root();
        FoundgineGatewayApprovalKeyProvider keys = keys(root);
        String keyId = c.get("keyId").asText();
        byte[] nonce = HexFormat.of().parseHex(c.get("nonceHex").asText());
        String sealed = ApprovalPayloadEnvelope.seal(c.get("plaintext").asText(), keyId, keys.key(keyId), nonce);
        assertEquals(c.get("envelope").asText(), sealed, c.get("name").asText());
        assertEquals(c.get("plaintext").asText(), ApprovalPayloadEnvelope.open(c.get("envelope").asText(), keys));
    }

    @ParameterizedTest @MethodSource("acceptCases")
    void open_acceptsPostgresJsonbCanonicalization(JsonNode c) throws Exception {
        FoundgineGatewayApprovalKeyProvider keys = keys(root());
        assertEquals(c.get("plaintext").asText(), ApprovalPayloadEnvelope.open(c.get("envelope").asText(), keys),
                c.get("name").asText());
    }

    @ParameterizedTest @MethodSource("rejectCases")
    void open_failsClosed(JsonNode c) throws Exception {
        FoundgineGatewayApprovalKeyProvider keys = keys(root());
        assertThrows(RuntimeException.class, () -> ApprovalPayloadEnvelope.open(c.get("envelope").asText(), keys),
                c.get("name").asText());
    }

    @Test
    void randomNonce_producesDistinctEnvelopesForTheSamePlaintext() throws Exception {
        JsonNode root = root();
        FoundgineGatewayApprovalKeyProvider keys = keys(root);
        byte[] key = keys.key("k1");
        String a = ApprovalPayloadEnvelope.seal("{}", "k1", key);
        String b = ApprovalPayloadEnvelope.seal("{}", "k1", key);
        assertNotEquals(a, b);
        assertEquals("{}", ApprovalPayloadEnvelope.open(a, keys));
    }

    @Test
    void rotation_oldKeyStillOpens_andRetiredKeyFailsClosed() throws Exception {
        JsonNode root = root();
        FoundgineGatewayApprovalKeyProvider keys = keys(root); // current = k1, k2 still known
        String oldEnvelope = ApprovalPayloadEnvelope.seal("{\"a\":1}", "k2", keys.key("k2"));
        assertEquals("{\"a\":1}", ApprovalPayloadEnvelope.open(oldEnvelope, keys));
        assertThrows(IllegalStateException.class, () -> ApprovalPayloadEnvelope.open(oldEnvelope.replace("\"k2\"", "\"k9\""), keys));
    }

    @Test
    void invalidKeyMaterial_isRejected() {
        assertThrows(IllegalStateException.class, () -> ApprovalPayloadEnvelope.seal("{}", "k1", new byte[16], new byte[12]));
        assertThrows(IllegalArgumentException.class, () -> ApprovalPayloadEnvelope.seal("{}", "bad id", new byte[32], new byte[12]));
        assertThrows(IllegalArgumentException.class, () -> ApprovalPayloadEnvelope.seal("{}", "k1", new byte[32], new byte[8]));
    }
}
