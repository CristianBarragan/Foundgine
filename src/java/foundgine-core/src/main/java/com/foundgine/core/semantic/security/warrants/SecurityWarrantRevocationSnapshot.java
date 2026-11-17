package com.foundgine.core.semantic.security.warrants;

public record SecurityWarrantRevocationSnapshot(long sequence) {
    public static SecurityWarrantRevocationSnapshot capture(ISecurityWarrantRevocationStore s) {
        if (s == null) throw new NullPointerException();
        return new SecurityWarrantRevocationSnapshot(s.currentSequence());
    }

    public void assertUnchanged(ISecurityWarrantRevocationStore s) {
        if (s == null) throw new NullPointerException();
        if (s.currentSequence() != sequence)
            throw new IllegalStateException(
                    "Authorization revocation state changed during execution.");
    }

    /**
     * Compares against a snapshot returned by a fresh, bracketed {@link SecurityWarrantRevocationGuard#validate},
     * avoiding a second round of store reads just to re-observe the sequence.
     */
    public void assertUnchanged(SecurityWarrantRevocationSnapshot current) {
        if (current == null) throw new NullPointerException();
        if (current.sequence() != sequence)
            throw new IllegalStateException(
                    "Authorization revocation state changed during execution.");
    }
}
