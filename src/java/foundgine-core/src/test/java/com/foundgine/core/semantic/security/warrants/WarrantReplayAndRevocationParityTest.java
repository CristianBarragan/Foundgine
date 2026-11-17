package com.foundgine.core.semantic.security.warrants;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;

class WarrantReplayAndRevocationParityTest {
    private static SecurityWarrant warrant() {
        var now = Instant.now();
        return SecurityWarrant.ofDefaults(
                "w-replay",
                "issuer",
                "agent",
                "audience",
                List.of(),
                new SecurityWarrantConstraints(),
                now.minusSeconds(5),
                now.plusSeconds(300),
                "nonce",
                "key",
                null,
                new byte[0]);
    }

    @Test
    void revocationInvalidatesPreviouslyValidWarrant() {
        var w = warrant();
        var store = new MemorySecurityWarrantRevocationStore();
        var now = Instant.now();

        SecurityWarrantRevocationGuard.validate(w, store, now);
        store.revoke(w, now);

        assertThrows(
                IllegalStateException.class,
                () -> SecurityWarrantRevocationGuard.validate(w, store, Instant.now()));
    }

    @Test
    void revocationSnapshotDetectsAuthorityChange() {
        var w = warrant();
        var store = new MemorySecurityWarrantRevocationStore();
        var snapshot = SecurityWarrantRevocationSnapshot.capture(store);

        store.revoke(w, Instant.now());

        assertThrows(IllegalStateException.class, () -> snapshot.assertUnchanged(store));
    }

    @Test
    void replayGuardRejectsSecondConsumption() {
        var w = warrant();
        var store = new MemorySecurityWarrantReplayStore();

        SecurityWarrantReplayGuard.consume(w, store, Instant.now());
        assertThrows(
                IllegalStateException.class,
                () -> SecurityWarrantReplayGuard.consume(w, store, Instant.now()));
    }

    private static final class BatchedFake implements IBatchedSecurityWarrantRevocationStore {
        final SecurityWarrantRevocationLookup result; int lookups;
        BatchedFake(SecurityWarrantRevocationLookup result) { this.result = result; }
        public long currentSequence() { throw new AssertionError("batched path must not bracket with currentSequence"); }
        public SecurityWarrantRevocation revoke(SecurityWarrant w, Instant now) { throw new UnsupportedOperationException(); }
        public boolean isRevoked(String id, String digest) { throw new AssertionError(); }
        public boolean isDigestRevoked(String digest) { throw new AssertionError(); }
        public SecurityWarrantRevocationLookup lookup(String id, String digest, List<String> ancestors) { lookups++; return result; }
    }

    @Test
    void guardUsesSingleBatchedLookupWhenAvailable() {
        var fake = new BatchedFake(new SecurityWarrantRevocationLookup(11, false, false));
        var snapshot = SecurityWarrantRevocationGuard.validate(warrant(), fake, Instant.now());
        assertEquals(11, snapshot.sequence());
        assertEquals(1, fake.lookups);
    }

    @Test
    void guardRejectsRevokedWarrantAndRevokedAncestorFromBatchedLookup() {
        var revoked = new BatchedFake(new SecurityWarrantRevocationLookup(1, true, false));
        var ex1 = assertThrows(IllegalStateException.class, () -> SecurityWarrantRevocationGuard.validate(warrant(), revoked, Instant.now()));
        assertTrue(ex1.getMessage().contains("has been revoked"));
        var ancestor = new BatchedFake(new SecurityWarrantRevocationLookup(1, false, true));
        var ex2 = assertThrows(IllegalStateException.class, () -> SecurityWarrantRevocationGuard.validate(warrant(), ancestor, Instant.now()));
        assertTrue(ex2.getMessage().contains("parent"));
    }

    @Test
    void snapshotComparesAgainstFreshSnapshotWithoutStoreReads() {
        var a = new SecurityWarrantRevocationSnapshot(5);
        a.assertUnchanged(new SecurityWarrantRevocationSnapshot(5));
        assertThrows(IllegalStateException.class, () -> a.assertUnchanged(new SecurityWarrantRevocationSnapshot(6)));
    }
}
