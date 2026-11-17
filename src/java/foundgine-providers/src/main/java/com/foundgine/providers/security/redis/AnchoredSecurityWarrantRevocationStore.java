package com.foundgine.providers.security.redis;

import com.foundgine.core.semantic.security.warrants.ISecurityWarrantRevocationEpochAnchor;
import com.foundgine.core.semantic.security.warrants.ISecurityWarrantRevocationStore;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantRevocation;
import java.time.Instant;
import java.util.Objects;

/** Requires Redis revocation state to agree with an independent durable monotonic epoch anchor. */
public final class AnchoredSecurityWarrantRevocationStore implements ISecurityWarrantRevocationStore {
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
    private long synchronizeAnchor() {
        long before = anchor.readHighWaterMark();
        if (before < 0) throw new IllegalStateException("Durable revocation anchor returned a negative high-water mark.");
        long observed = inner.currentSequence();
        if (observed < before) throw new IllegalStateException("Redis revocation sequence " + observed + " is below durable high-water mark " + before + "; failing closed after possible state rollback.");
        anchor.advanceHighWaterMark(observed);
        long after = anchor.readHighWaterMark();
        if (after < observed) throw new IllegalStateException("Durable revocation anchor did not persist the observed sequence; failing closed.");
        if (observed < after) throw new IllegalStateException("Redis revocation sequence is behind a concurrently advanced durable high-water mark; failing closed.");
        return observed;
    }
}
