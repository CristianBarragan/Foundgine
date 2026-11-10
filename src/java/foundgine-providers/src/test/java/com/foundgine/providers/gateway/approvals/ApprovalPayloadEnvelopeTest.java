package com.foundgine.providers.gateway.approvals;

import static org.junit.jupiter.api.Assertions.*;

import java.util.HashMap;
import java.util.Map;
import org.junit.jupiter.api.Test;

/** DB-free tests for the AES-256-GCM approval envelope, including the vector the .NET suite must also decrypt. */
class ApprovalPayloadEnvelopeTest {
    /** Produced by this implementation with key bytes 7..38 and nonce bytes 0..11; the .NET test embeds the same value. */
    static final String VECTOR = "{\"v\":1,\"kid\":\"vec-1\",\"n\":\"AAECAwQFBgcICQoL\",\"c\":\"WB4POqmwxqUIbQzVCLv/QmgRFb0cT6SGYNzsTf+L+tU=\"}";

    private static byte[] key(int seed) { var b = new byte[32]; for (int i = 0; i < 32; i++) b[i] = (byte) (seed + i); return b; }

    private final Map<String, byte[]> keys = new HashMap<>(Map.of("k1", key(1), "k2", key(50), "vec-1", key(7)));
    private final FoundgineGatewayApprovalKeyProvider provider = new FoundgineGatewayApprovalKeyProvider() {
        @Override public String currentKeyId() { return "k1"; }
        @Override public byte[] key(String id) {
            var k = keys.get(id);
            if (k == null) throw new IllegalStateException("unknown or retired key " + id);
            return k;
        }
    };

    @Test void roundTripHidesPlaintextAndUsesAFreshNonce() {
        var env = ApprovalPayloadEnvelope.seal("{\"subject\":\"alice\"}", "k1", key(1));
        assertFalse(env.contains("alice"));
        assertEquals("{\"subject\":\"alice\"}", ApprovalPayloadEnvelope.open(env, provider));
        assertNotEquals(env, ApprovalPayloadEnvelope.seal("{\"subject\":\"alice\"}", "k1", key(1)));
    }

    @Test void tamperedCiphertextAndRelabelledKeyIdFailClosed() {
        var env = ApprovalPayloadEnvelope.seal("secret", "k1", key(1));
        int at = env.indexOf("\"c\":\"") + 5;
        var tampered = env.substring(0, at) + (env.charAt(at) == 'A' ? 'B' : 'A') + env.substring(at + 1);
        assertThrows(IllegalStateException.class, () -> ApprovalPayloadEnvelope.open(tampered, provider));
        assertThrows(IllegalStateException.class,
                () -> ApprovalPayloadEnvelope.open(env.replace("\"kid\":\"k1\"", "\"kid\":\"k2\""), provider));
    }

    @Test void retiredKeyFailsClosedAndRotationKeepsOldPayloadsReadable() {
        var env = ApprovalPayloadEnvelope.seal("secret", "k1", key(1));
        assertEquals("secret", ApprovalPayloadEnvelope.open(env, provider)); // current key rotated to k2 later: k1 still known
        keys.remove("k1");
        assertThrows(IllegalStateException.class, () -> ApprovalPayloadEnvelope.open(env, provider));
    }

    @Test void malformedShapesAndBadInputsAreRejected() {
        assertThrows(IllegalStateException.class, () -> ApprovalPayloadEnvelope.open("{\"v\":2}", provider));
        assertThrows(IllegalStateException.class,
                () -> ApprovalPayloadEnvelope.open(ApprovalPayloadEnvelope.seal("x", "k1", key(1)) + "x", provider));
        assertThrows(IllegalArgumentException.class, () -> ApprovalPayloadEnvelope.seal("x", "bad id", key(1)));
        assertThrows(IllegalStateException.class, () -> ApprovalPayloadEnvelope.seal("x", "k1", new byte[16]));
    }


    @Test void postgresJsonbCanonicalizationIsAcceptedButExtraFieldsAreRejected() {
        var env = ApprovalPayloadEnvelope.seal("payload", "k1", key(1));
        var fields = env.substring(1, env.length() - 1).split(",");
        var reordered = "{ " + fields[3] + ", " + fields[2] + ", " + fields[1] + ", " + fields[0] + " }";
        assertEquals("payload", ApprovalPayloadEnvelope.open(reordered, provider));
        assertEquals("payload", ApprovalPayloadEnvelope.open(env + "  ", provider));
        var extra = env.substring(0, env.length() - 1) + ",\"extra\":true}";
        assertThrows(IllegalStateException.class, () -> ApprovalPayloadEnvelope.open(extra, provider));
        var duplicate = env.substring(0, env.length() - 1) + ",\"v\":1}";
        assertThrows(IllegalStateException.class, () -> ApprovalPayloadEnvelope.open(duplicate, provider));
    }

    @Test void crossRuntimeVectorDecrypts() {
        assertEquals("foundgine-vector", ApprovalPayloadEnvelope.open(VECTOR, provider));
        var nonce = new byte[12]; for (int i = 0; i < 12; i++) nonce[i] = (byte) i;
        assertEquals(VECTOR, ApprovalPayloadEnvelope.seal("foundgine-vector", "vec-1", key(7), nonce));
    }
}
