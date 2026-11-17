package com.foundgine.providers.security.redis;

import com.foundgine.core.semantic.security.warrants.IBatchedSecurityWarrantRevocationStore;
import com.foundgine.core.semantic.security.warrants.ISecurityWarrantRevocationEpochAnchor;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantRevocation;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantRevocationLookup;
import com.foundgine.core.semantic.security.warrants.ISecurityWarrantRevocationStore;
import java.time.Instant;
import java.util.List;
import java.util.Objects;

/** Requires Redis revocation state to agree with an independent durable monotonic epoch anchor. */
public final class AnchoredSecurityWarrantRevocationStore implements IBatchedSecurityWarrantRevocationStore {
    private final ISecurityWarrantRevocationStore inner;
    private final ISecurityWarrantRevocationEpochAnchor anchor;

    public AnchoredSecurityWarrantRevocationStore(ISecurityWarrantRevocationStore inner, ISecurityWarrantRevocationEpochAnchor anchor) {
        this.inner = Objects.requireNonNull(inner, "inner"); this.anchor = Objects.requireNonNull(anchor, "anchor");
    }
    @Override public long currentSequence() { return synchronizeAnchor(); }
    @Override public SecurityWarrantRevocation revoke(SecurityWarrant warrant, Instant now) {
        SecurityWarrantRevocation result = inner.revoke(warrant, now);
        long anchored = synchronizeAnchor();
        if (anchored < result.sequence()) throw new IllegalStateException("Durable revocation anchor is behind the committed Redis record; failing closed.");
        return result;
    }
    @Override public boolean isRevoked(String id, String digest) {
        synchronizeAnchor(); boolean result = inner.isRevoked(id, digest); synchronizeAnchor(); return result;
    }
    @Override public boolean isDigestRevoked(String digest) {
        synchronizeAnchor(); boolean result = inner.isDigestRevoked(digest); synchronizeAnchor(); return result;
    }
    /**
     * One anchor read plus one inner lookup. When the inner store can answer atomically, the lookup result and its
     * sequence are a single snapshot, so the per-question anchor bracketing is not needed.
     */
    @Override public SecurityWarrantRevocationLookup lookup(String id, String digest, List<String> ancestorDigests) {
        Objects.requireNonNull(ancestorDigests, "ancestorDigests");
        if (!(inner instanceof IBatchedSecurityWarrantRevocationStore batched)) {
            long before = synchronizeAnchor();
            boolean revoked = isRevoked(id, digest) || isDigestRevoked(digest);
            boolean ancestorRevoked = false;
            if (!revoked) for (String ancestor : ancestorDigests) if (isDigestRevoked(ancestor)) { ancestorRevoked = true; break; }
            long after = synchronizeAnchor();
            if (after != before) throw new IllegalStateException("Authorization revocation state changed during warrant validation.");
            return new SecurityWarrantRevocationLookup(after, revoked, ancestorRevoked);
        }
        long durableBefore = readAnchor();
        SecurityWarrantRevocationLookup lookup = batched.lookup(id, digest, ancestorDigests);
        ensureAnchored(durableBefore, lookup.sequence());
        return lookup;
    }

    private long readAnchor() {
        long value = anchor.readHighWaterMark();
        if (value < 0) throw new IllegalStateException("Durable revocation anchor returned a negative high-water mark.");
        return value;
    }

    private long synchronizeAnchor() { return ensureAnchored(readAnchor(), inner.currentSequence()); }

    private long ensureAnchored(long before, long observed) {
        if (observed < before) throw new IllegalStateException("Redis revocation sequence " + observed + " is below durable high-water mark " + before + "; failing closed after possible state rollback.");
        // Steady state: Redis and the durable anchor agree, so there is nothing to persist. Skipping the write keeps
        // authorization reads from serializing on one shared row. Rollback was rejected above.
        if (observed == before) return observed;
        anchor.advanceHighWaterMark(observed);
        long after = anchor.readHighWaterMark();
        if (after < observed) throw new IllegalStateException("Durable revocation anchor did not persist the observed sequence; failing closed.");
        if (observed < after) throw new IllegalStateException("Redis revocation sequence is behind a concurrently advanced durable high-water mark; failing closed.");
        return observed;
    }
}
