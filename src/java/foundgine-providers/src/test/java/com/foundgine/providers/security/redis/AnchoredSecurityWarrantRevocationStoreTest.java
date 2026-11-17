package com.foundgine.providers.security.redis;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.semantic.security.warrants.ISecurityWarrantRevocationEpochAnchor;
import com.foundgine.core.semantic.security.warrants.ISecurityWarrantRevocationStore;
import java.time.Instant;
import org.junit.jupiter.api.Test;

class AnchoredSecurityWarrantRevocationStoreTest {
    @Test void advancesDurableMarkAfterRedisMutation() {
        var inner = new SequenceStore(12);
        var anchor = new MemoryAnchor();
        var store = new AnchoredSecurityWarrantRevocationStore(inner, anchor);
        assertEquals(12, store.currentSequence());
        assertEquals(12, anchor.value);
    }

    @Test void failsClosedWhenRedisSequenceIsBehindDurableMark() {
        var anchor = new MemoryAnchor(); anchor.value = 20;
        var store = new AnchoredSecurityWarrantRevocationStore(new SequenceStore(19), anchor);
        assertThrows(IllegalStateException.class, store::currentSequence);
    }

    @Test void failsClosedWhenAnchorCannotBeRead() {
        ISecurityWarrantRevocationEpochAnchor anchor = new ISecurityWarrantRevocationEpochAnchor() {
            public long readHighWaterMark() { throw new IllegalStateException("offline"); }
            public void advanceHighWaterMark(long sequence) { fail("must not advance"); }
        };
        assertThrows(IllegalStateException.class, () -> new AnchoredSecurityWarrantRevocationStore(new SequenceStore(0), anchor).currentSequence());
    }

    private static final class MemoryAnchor implements ISecurityWarrantRevocationEpochAnchor {
        long value;
        public long readHighWaterMark() { return value; }
        public void advanceHighWaterMark(long sequence) { value = Math.max(value, sequence); }
    }
    private static final class SequenceStore implements ISecurityWarrantRevocationStore {
        final long sequence;
        SequenceStore(long sequence) { this.sequence = sequence; }
        public long currentSequence() { return sequence; }
        public com.foundgine.core.semantic.security.warrants.SecurityWarrantRevocation revoke(com.foundgine.core.semantic.security.warrants.SecurityWarrant w, Instant now) { throw new UnsupportedOperationException(); }
        public boolean isRevoked(String id, String digest) { return false; }
        public boolean isDigestRevoked(String digest) { return false; }
    }
}
