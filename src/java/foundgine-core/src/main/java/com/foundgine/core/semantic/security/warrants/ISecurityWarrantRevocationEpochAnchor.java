package com.foundgine.core.semantic.security.warrants;

/** Durable, shared monotonic high-water authority for warrant revocation sequences. */
public interface ISecurityWarrantRevocationEpochAnchor {
    /** Reads the authoritative value; a new installation may return zero. */
    long readHighWaterMark();

    /** Atomically stores max(current value, sequence) and commits durably before returning. */
    void advanceHighWaterMark(long sequence);
}
