package com.foundgine.providers.tools.mcp;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.GeneralSecurityException;
import java.security.KeyFactory;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.PublicKey;
import java.security.spec.PKCS8EncodedKeySpec;
import java.security.spec.X509EncodedKeySpec;
import java.time.Duration;
import java.time.Instant;
import java.util.Base64;
import java.util.concurrent.atomic.AtomicLong;
import org.junit.jupiter.api.Test;

class McpApprovalKeyRotationTest {
    private static final String REQUEST = "{\"operations\":[{\"entity\":1,\"kind\":\"Create\",\"fields\":{\"2\":\"x\"}}]}";
    private static final Duration FIVE_MIN_30S = Duration.ofMinutes(5).plusSeconds(30);

    private static KeyPair newKey() throws Exception {
        KeyPairGenerator g = KeyPairGenerator.getInstance("RSA");
        g.initialize(2048);
        return g.generateKeyPair();
    }

    // A deliberately undersized (1024-bit) RSA key, kept as a fixed fixture so no code path in the tests generates one: the key
    // ring must refuse it. Test-only material; it protects nothing.
    private static final String WEAK_PRIVATE_PKCS8 = "MIICeAIBADANBgkqhkiG9w0BAQEFAASCAmIwggJeAgEAAoGBANzWxkGHng1ubwuwFjTuxrwWMSu7uJfF"
            + "e8it3LJcicGkzrAcfFeC5kwJkTGY6lfs/uMCMd6KFbc04OfUI+aXoH3xbWKoBqhZ/D9ecR9UMvvFdGNH"
            + "OU7jkKe8CWnlTNlF/d030Fvy8tJFMVovOKetadi6IV6h0WSvlJx6VZBkIRx/AgMBAAECgYA1Ye12tbOt"
            + "B37sUlpr3TRJDww/yBbtCR3M+soYi2n5Vz9nLtuXw9qrkrx4QsCTFITglw07ACITsUKUh6FOAmUW2aac"
            + "q7LLJD4ergjbge56XT39zv826URlj9drTxE6qU8aKxi7Gq3yHI0KZlT+oLxfF2ALLhYlggvKH7N7zdW2"
            + "8QJBAPIp6WCH1aaYc11m4u8dYshYVFHJBGYjMw7exZsDWqp12MA80L4NWdFbH/ayXpJCmeIuo+RpOZPe"
            + "c4RDTUw2em0CQQDpdPL8+eF8dY65deX+NnYe8Sdoivzy0mhLWlvKW/QDqt2uT3n1Laih33zXOKv5hnXj"
            + "HLxBu35kP0FORtIuax8bAkEA3VpkIcFoELTvVuQR8WHaBql+wZdONxJbvG7zD4OcX44DB41PfTl07UIv"
            + "UG+wtF6+jJn3Y3SvlbNTwgVG5bAybQJBANV8r/QtIklpS3JH4WG+NfiHuOz+JOB9DwjRgZ0kx5lO+Fe9"
            + "sB5IeaxFrmk2FvYhQs7EaVeG+Mb9nV/SncP7C78CQQDxkHBwd+H6vR+NO5aEOEcOf9lHHFNRwlqhjh8m"
            + "yPaCIQ02pH4vxLZ+2oTK1HyYPvbI+ZtfgkZM3DJafteX2Vvw";
    private static final String WEAK_PUBLIC_X509 = "MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDc1sZBh54Nbm8LsBY07sa8FjEru7iXxXvIrdyyXInB"
            + "pM6wHHxXguZMCZExmOpX7P7jAjHeihW3NODn1CPml6B98W1iqAaoWfw/XnEfVDL7xXRjRzlO45CnvAlp"
            + "5UzZRf3dN9Bb8vLSRTFaLzinrWnYuiFeodFkr5ScelWQZCEcfwIDAQAB";

    private static KeyPair weakKey() throws Exception {
        KeyFactory f = KeyFactory.getInstance("RSA");
        var pk = f.generatePrivate(new PKCS8EncodedKeySpec(Base64.getDecoder().decode(WEAK_PRIVATE_PKCS8)));
        var pub = f.generatePublic(new X509EncodedKeySpec(Base64.getDecoder().decode(WEAK_PUBLIC_X509)));
        return new KeyPair(pub, pk);
    }

    private static String token(FoundgineMcpApprovalKeyRing ring, String approvalId) throws Exception {
        return FoundgineMcpApprovalToken.encode(approvalId, "sha256:abc", "alice", 1_000L, REQUEST, ring);
    }

    @Test void signsWithActiveKeyAndVerifiesWithSameRing() throws Exception {
        var ring = new FoundgineMcpApprovalKeyRing();
        KeyPair k1 = newKey();
        ring.activateSigningKey("k1", k1.getPrivate(), k1.getPublic());
        var claims = FoundgineMcpApprovalToken.decodeAndVerify(token(ring, "a1"), ring);
        assertEquals("k1", claims.keyId());
        assertEquals("a1", claims.approvalId());
    }

    @Test void rotationKeepsOutstandingTokensVerifiableUntilRetentionEnds() throws Exception {
        var now = new AtomicLong(1_000_000L);
        var ring = new FoundgineMcpApprovalKeyRing(FIVE_MIN_30S, now::get);
        KeyPair k1 = newKey(), k2 = newKey();
        ring.activateSigningKey("k1", k1.getPrivate(), k1.getPublic());
        String oldToken = token(ring, "old");
        ring.activateSigningKey("k2", k2.getPrivate(), k2.getPublic(), FIVE_MIN_30S);
        String newToken = token(ring, "new");

        assertEquals("k2", FoundgineMcpApprovalToken.decodeAndVerify(newToken, ring).keyId());
        assertEquals("k1", FoundgineMcpApprovalToken.decodeAndVerify(oldToken, ring).keyId());
        assertEquals("k2", ring.keyId());

        now.addAndGet(FIVE_MIN_30S.toMillis() - 1);
        assertDoesNotThrow(() -> FoundgineMcpApprovalToken.decodeAndVerify(oldToken, ring));
        now.addAndGet(1);
        assertThrows(GeneralSecurityException.class, () -> FoundgineMcpApprovalToken.decodeAndVerify(oldToken, ring));
        assertDoesNotThrow(() -> FoundgineMcpApprovalToken.decodeAndVerify(newToken, ring));
    }

    @Test void retiredKeyCanNoLongerSign() throws Exception {
        var ring = new FoundgineMcpApprovalKeyRing();
        KeyPair k1 = newKey(), k2 = newKey();
        ring.activateSigningKey("k1", k1.getPrivate(), k1.getPublic());
        var pinned = ring.snapshot();
        ring.activateSigningKey("k2", k2.getPrivate(), k2.getPublic(), Duration.ofMinutes(10));
        assertEquals("k1", pinned.keyId());
        assertEquals("k2", ring.snapshot().keyId());
        var infos = ring.keys();
        assertEquals(2, infos.size());
        assertEquals(FoundgineMcpApprovalKeyRing.Role.SIGNING, infos.get(0).role());
        assertEquals("k2", infos.get(0).keyId());
        assertEquals(FoundgineMcpApprovalKeyRing.Role.VERIFY_ONLY, infos.get(1).role());
    }

    @Test void retentionBelowPolicyMinimumIsRejectedAndNothingChanges() throws Exception {
        var policy = FoundgineMcpApprovalTokenPolicy.defaults((id, exp) -> true);
        assertEquals(FIVE_MIN_30S, policy.minimumKeyRetention());
        var ring = FoundgineMcpApprovalKeyRing.forPolicy(policy);
        KeyPair k1 = newKey(), k2 = newKey();
        ring.activateSigningKey("k1", k1.getPrivate(), k1.getPublic());
        assertThrows(IllegalArgumentException.class,
                () -> ring.activateSigningKey("k2", k2.getPrivate(), k2.getPublic(), Duration.ofMinutes(1)));
        assertEquals("k1", ring.keyId());
        assertNull(ring.resolve("k2"));
        assertThrows(IllegalStateException.class, () -> ring.activateSigningKey("k2", k2.getPrivate(), k2.getPublic()));
    }

    @Test void revocationIsImmediateAndKeyIdsAreNeverReused() throws Exception {
        var ring = new FoundgineMcpApprovalKeyRing();
        KeyPair k1 = newKey(), k2 = newKey();
        ring.activateSigningKey("k1", k1.getPrivate(), k1.getPublic());
        String t = token(ring, "a");
        ring.activateSigningKey("k2", k2.getPrivate(), k2.getPublic(), Duration.ofDays(1));
        assertTrue(ring.revoke("k1"));
        assertNull(ring.resolve("k1"));
        assertThrows(GeneralSecurityException.class, () -> FoundgineMcpApprovalToken.decodeAndVerify(t, ring));
        KeyPair attacker = newKey();
        assertThrows(IllegalArgumentException.class, () -> ring.addVerificationKey("k1", attacker.getPublic(), null));
        // revoking the signing key stops signing until a new key is activated
        assertTrue(ring.revoke("k2"));
        assertThrows(IllegalStateException.class, ring::keyId);
        KeyPair k3 = newKey();
        ring.activateSigningKey("k3", k3.getPrivate(), k3.getPublic());
        assertEquals("k3", ring.keyId());
    }

    @Test void verifyOnlyHostsFollowRotationAndNeverSign() throws Exception {
        var now = new AtomicLong(5_000L);
        var verifier = new FoundgineMcpApprovalKeyRing(Duration.ofMinutes(1), now::get);
        KeyPair k1 = newKey(), k2 = newKey();
        var signer = new FoundgineMcpApprovalKeyRing();
        signer.activateSigningKey("k1", k1.getPrivate(), k1.getPublic());
        verifier.addVerificationKey("k1", k1.getPublic(), null);
        assertThrows(IllegalStateException.class, verifier::keyId);
        String t1 = token(signer, "a");
        assertEquals("a", FoundgineMcpApprovalToken.decodeAndVerify(t1, verifier).approvalId());

        verifier.addVerificationKey("k2", k2.getPublic(), null);
        assertThrows(IllegalArgumentException.class, () -> verifier.retireAfter("k1", Duration.ofSeconds(1)));
        assertTrue(verifier.retireAfter("k1", Duration.ofMinutes(5)));
        assertTrue(verifier.retireAfter("k1", Duration.ofMinutes(10)));   // never extends
        now.addAndGet(Duration.ofMinutes(5).toMillis());
        assertThrows(GeneralSecurityException.class, () -> FoundgineMcpApprovalToken.decodeAndVerify(t1, verifier));
        assertFalse(verifier.retireAfter("k1", Duration.ofMinutes(5)));
        assertThrows(IllegalArgumentException.class,
                () -> verifier.addVerificationKey("late", k2.getPublic(), Instant.ofEpochMilli(now.get())));
    }

    @Test void rejectsWeakMismatchedAndNonRsaKeys() throws Exception {
        var ring = new FoundgineMcpApprovalKeyRing();
        KeyPair weak = weakKey(), a = newKey(), b = newKey();
        assertThrows(IllegalArgumentException.class, () -> ring.activateSigningKey("w", weak.getPrivate(), weak.getPublic()));
        assertThrows(IllegalArgumentException.class, () -> ring.activateSigningKey("m", a.getPrivate(), b.getPublic()));
        assertThrows(IllegalArgumentException.class, () -> ring.activateSigningKey(" ", a.getPrivate(), a.getPublic()));
        assertThrows(IllegalStateException.class, ring::keyId);
        assertNull(ring.resolve(null));
        assertNull(ring.resolve("nope"));
    }

    @Test void tokenSignedByNewKeyIdFailsUnderDifferentMaterialWithSameId() throws Exception {
        var ring = new FoundgineMcpApprovalKeyRing();
        KeyPair k1 = newKey(), other = newKey();
        var foreign = new FoundgineMcpApprovalKeyRing();
        foreign.activateSigningKey("k1", other.getPrivate(), other.getPublic());
        ring.activateSigningKey("k1", k1.getPrivate(), k1.getPublic());
        String forged = token(foreign, "forged");
        assertThrows(GeneralSecurityException.class, () -> FoundgineMcpApprovalToken.decodeAndVerify(forged, ring));
    }

    @Test void sharedGoldenVectorStillVerifiesThroughTheRing() throws Exception {
        JsonNode v = new ObjectMapper().readTree(Files.readString(Path.of("../../conformance/mcp-approval-token-golden.json")));
        String pub = v.path("publicKeyPem").asText().replace("-----BEGIN PUBLIC KEY-----", "")
                .replace("-----END PUBLIC KEY-----", "").replaceAll("\\s", "");
        String priv = v.path("privateKeyPem_TEST_ONLY").asText().replace("-----BEGIN PRIVATE KEY-----", "")
                .replace("-----END PRIVATE KEY-----", "").replaceAll("\\s", "");
        KeyFactory kf = KeyFactory.getInstance("RSA");
        PublicKey publicKey = kf.generatePublic(new X509EncodedKeySpec(Base64.getDecoder().decode(pub)));
        PrivateKey privateKey = kf.generatePrivate(new PKCS8EncodedKeySpec(Base64.getDecoder().decode(priv)));
        var ring = new FoundgineMcpApprovalKeyRing();
        ring.activateSigningKey("golden-key-1", privateKey, publicKey);
        assertEquals("golden-approval-1",
                FoundgineMcpApprovalToken.decodeAndVerify(v.path("token").asText(), ring).approvalId());
        JsonNode c = v.path("claims");
        String regenerated = FoundgineMcpApprovalToken.encode(c.path("approvalId").asText(),
                c.path("planFingerprint").asText(), c.path("approvedBy").asText(),
                c.path("approvedAtUnixMilliseconds").asLong(), c.path("requestJson").asText(), ring);
        assertEquals(v.path("token").asText(), regenerated);
    }
}
