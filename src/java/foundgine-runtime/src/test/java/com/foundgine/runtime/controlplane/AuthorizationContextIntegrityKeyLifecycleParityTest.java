package com.foundgine.runtime.controlplane;

import org.junit.jupiter.api.Test;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.HashSet;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.stream.IntStream;

import static org.junit.jupiter.api.Assertions.*;

class AuthorizationContextIntegrityKeyLifecycleParityTest {
    private static AuthorizationContextIntegrityKey key(String id, String material) {
        try {
            return new AuthorizationContextIntegrityKey(id,
                    MessageDigest.getInstance("SHA-256")
                            .digest(("Foundgine-:" + material).getBytes(StandardCharsets.UTF_8)));
        } catch (Exception e) {
            throw new AssertionError(e);
        }
    }

    private static AuthorizationKeyRotationProvenance provenance(long sequence) {
        return new AuthorizationKeyRotationProvenance(UUID.randomUUID(), sequence, "rotation-credential");
    }

    @Test
    void rotationMakesPreviousActiveKeyVerificationOnly() {
        var ring = new AuthorizationContextIntegrityKeyRing(key("key-v1", "old"));
        var rotated = ring.rotate(key("key-v2", "new"), provenance(1));
        assertEquals("key-v2", rotated.activeKeyId());
        assertEquals(AuthorizationIntegrityKeyState.VERIFICATION_ONLY, rotated.getState("key-v1"));
        assertEquals(AuthorizationIntegrityKeyState.ACTIVE, rotated.getState("key-v2"));
        assertTrue(rotated.canVerify("key-v1"));
        assertEquals(2, rotated.configurationVersion());
    }

    @Test
    void retiredKeyCannotVerifyExistingEvidence() {
        var actor = UUID.randomUUID();
        var ring = new AuthorizationContextIntegrityKeyRing(key("key-v1", "old"));
        var tag = ring.computeContextTag(actor, 10, true, 1, "fp");
        ring = ring.rotate(key("key-v2", "new"), provenance(1));
        ring = ring.retire("key-v1", provenance(2), new HashSet<>());
        assertEquals(AuthorizationIntegrityKeyState.RETIRED, ring.getState("key-v1"));
        assertFalse(ring.verifyContextTag(actor, 10, true, 1, "fp",
                AuthorizationContextIntegrityKeyRing.currentAlgorithmVersion(), "key-v1", tag));
    }

    @Test
    void activeKeyCannotBeRetired() {
        var ring = new AuthorizationContextIntegrityKeyRing(key("key-v1", "old"));
        var ex = assertThrows(IllegalStateException.class,
                () -> ring.retire("key-v1", provenance(1), new HashSet<>()));
        assertTrue(ex.getMessage().toLowerCase().contains("active"));
    }

    @Test
    void persistedEvidencePreventsRetirement() {
        var ring = new AuthorizationContextIntegrityKeyRing(key("key-v1", "old"))
                .rotate(key("key-v2", "new"), provenance(1));
        var ex = assertThrows(IllegalStateException.class,
                () -> ring.retire("key-v1", provenance(2), Set.of("key-v1")));
        assertTrue(ex.getMessage().toLowerCase().contains("referenced"));
    }

    @Test
    void rotationSequenceIsMonotonic() {
        var ring = new AuthorizationContextIntegrityKeyRing(key("key-v1", "old"))
                .rotate(key("key-v2", "new"), provenance(7));
        var ex = assertThrows(IllegalStateException.class,
                () -> ring.rotate(key("key-v3", "newer"), provenance(7)));
        assertTrue(ex.getMessage().toLowerCase().contains("stale"));
    }

    @Test
    void retiredKeyCannotBeReactivated() {
        var ring = new AuthorizationContextIntegrityKeyRing(key("key-v1", "old"))
                .rotate(key("key-v2", "new"), provenance(1))
                .retire("key-v1", provenance(2), new HashSet<>());
        var ex = assertThrows(IllegalStateException.class,
                () -> ring.rotate(key("key-v1", "old"), provenance(3)));
        assertTrue(ex.getMessage().toLowerCase().contains("retired"));
    }

    @Test
    void unauthorizedOperatorCannotRotate() {
        var operator = UUID.randomUUID();
        var manager = new AuthorizationContextIntegrityKeyRingManager(
                new AuthorizationContextIntegrityKeyRing(key("key-v1", "old")),
                new AuthorizationKeyRotationAuthorizer(Map.of(operator, "real-credential")));
        assertThrows(SecurityException.class, () -> manager.rotate(
                key("key-v2", "new"), new AuthorizationKeyRotationProvenance(operator, 1, "forged-credential")));
        assertThrows(SecurityException.class, () -> manager.rotate(
                key("key-v2", "new"), new AuthorizationKeyRotationProvenance(UUID.randomUUID(), 1, "real-credential")));
    }

    @Test
    void concurrentRotationWithSameSequenceAllowsOnlyOneCommit() throws InterruptedException {
        var operator = UUID.randomUUID();
        var manager = new AuthorizationContextIntegrityKeyRingManager(
                new AuthorizationContextIntegrityKeyRing(key("key-v1", "old")),
                new AuthorizationKeyRotationAuthorizer(Map.of(operator, "rotation-credential")));
        var successes = new AtomicInteger();
        var failures = new AtomicInteger();
        var threads = IntStream.range(0, 32).mapToObj(i -> new Thread(() -> {
            try {
                manager.rotate(key("key-v2", "new"), new AuthorizationKeyRotationProvenance(operator, 1, "rotation-credential"));
                successes.incrementAndGet();
            } catch (IllegalStateException ex) {
                failures.incrementAndGet();
            }
        })).toList();
        threads.forEach(Thread::start);
        for (var thread : threads) thread.join();
        assertEquals(1, successes.get());
        assertEquals(31, failures.get());
        assertEquals("key-v2", manager.snapshot().activeKeyId());
        assertEquals(1, manager.snapshot().lastRotationSequence());
    }

    @Test
    void rotationPublishesAtomicImmutableSnapshot() {
        var operator = UUID.randomUUID();
        var manager = new AuthorizationContextIntegrityKeyRingManager(
                new AuthorizationContextIntegrityKeyRing(key("key-v1", "old")),
                new AuthorizationKeyRotationAuthorizer(Map.of(operator, "rotation-credential")));
        var before = manager.snapshot();
        var after = manager.rotate(key("key-v2", "new"),
                new AuthorizationKeyRotationProvenance(operator, 1, "rotation-credential"));
        assertNotSame(before, after);
        assertEquals("key-v1", before.activeKeyId());
        assertEquals(0, before.lastRotationSequence());
        assertEquals("key-v2", after.activeKeyId());
        assertEquals(1, after.lastRotationSequence());
    }

    @Test
    void invalidRotationProvenanceFailsClosed() {
        var ring = new AuthorizationContextIntegrityKeyRing(key("key-v1", "old"));
        assertThrows(SecurityException.class, () -> ring.rotate(key("key-v2", "new"),
                new AuthorizationKeyRotationProvenance(AuthorizationKeyRotationProvenance.EMPTY_OPERATOR_ID, 1, "credential")));
        assertThrows(IllegalArgumentException.class, () -> ring.rotate(key("key-v2", "new"),
                new AuthorizationKeyRotationProvenance(UUID.randomUUID(), 0, "credential")));
    }
}
