package com.foundgine.core.semantic.security.warrants;

import java.time.Instant;

public final class SecurityWarrantRevocationGuard {
    private SecurityWarrantRevocationGuard() {}

    public static SecurityWarrantRevocationSnapshot validate(
            SecurityWarrant w, ISecurityWarrantRevocationStore s, Instant now) {
        if (w == null || s == null) throw new NullPointerException();
        if (!w.isTimeValid(now))
            throw new IllegalStateException("Security warrant is expired or not yet valid.");
        if (s instanceof IBatchedSecurityWarrantRevocationStore batched) {
            // The lookup is one consistent snapshot, so no separate before/after sequence bracket is needed.
            SecurityWarrantRevocationLookup lookup = batched.lookup(w.id(), w.digest(), w.delegationPath());
            if (lookup.revoked())
                throw new IllegalStateException("Security warrant has been revoked.");
            if (lookup.ancestorRevoked())
                throw new IllegalStateException("A parent security warrant has been revoked.");
            return new SecurityWarrantRevocationSnapshot(lookup.sequence());
        }
        // Bracket every lookup with the sequence. Capturing only after the lookups leaves a
        // check-then-capture race in which a concurrent revocation can be missed.
        long before = s.currentSequence();
        if (s.isRevoked(w.id(), w.digest()) || s.isDigestRevoked(w.digest()))
            throw new IllegalStateException("Security warrant has been revoked.");
        for (String d : w.delegationPath())
            if (s.isDigestRevoked(d))
                throw new IllegalStateException("A parent security warrant has been revoked.");
        long after = s.currentSequence();
        if (after != before)
            throw new IllegalStateException("Authorization revocation state changed during warrant validation.");
        return new SecurityWarrantRevocationSnapshot(after);
    }
}
