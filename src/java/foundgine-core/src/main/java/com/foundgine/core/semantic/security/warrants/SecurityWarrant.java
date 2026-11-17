package com.foundgine.core.semantic.security.warrants;

import java.time.Instant;
import java.util.*;

public record SecurityWarrant(
        String id,
        String issuer,
        String subject,
        String audience,
        List<CapabilityGrant> grants,
        SecurityWarrantConstraints constraints,
        Instant issuedAt,
        Instant expiresAt,
        String nonce,
        String keyId,
        String parentId,
        byte[] signature,
        String parentDigest,
        List<String> delegationPath) {
    public SecurityWarrant {
        grants = List.copyOf(grants == null ? List.of() : grants);
        constraints = Objects.requireNonNull(constraints, "constraints");
        issuedAt = tick(issuedAt);
        expiresAt = tick(expiresAt);
        delegationPath = List.copyOf(delegationPath == null ? List.of() : delegationPath);
        signature = signature == null ? new byte[0] : signature.clone();
    }

    /** Truncates to 100 ns, the resolution of the canonical form and of .NET ticks, so every comparison agrees. */
    private static Instant tick(Instant value) {
        return value == null ? null : Instant.ofEpochSecond(value.getEpochSecond(), value.getNano() / 100 * 100);
    }

    public static SecurityWarrant ofDefaults(
            String id,
            String issuer,
            String subject,
            String audience,
            List<CapabilityGrant> grants,
            SecurityWarrantConstraints constraints,
            Instant issuedAt,
            Instant expiresAt,
            String nonce,
            String keyId,
            String parentId,
            byte[] signature) {
        return new SecurityWarrant(
                id,
                issuer,
                subject,
                audience,
                grants,
                constraints,
                issuedAt,
                expiresAt,
                nonce,
                keyId,
                parentId,
                signature,
                null,
                List.of());
    }

    public int delegationDepth() {
        return delegationPath.size();
    }

    /**
     * Digest of the canonical unsigned form. The digest is read on every revocation, replay, cache-key and
     * verification step of a request, and rebuilding the canonical bytes and re-hashing them each time is repeated
     * CPU cost. A record cannot hold a cache field, so recent results are memoised per thread, keyed by instance
     * identity. Warrants are immutable (collections are copied, the signature is cloned and excluded from the
     * digest), so a hit for the same instance is always correct, and a modified copy is a different instance.
     */
    public String digest() {
        DigestMemo memo = DIGEST_MEMO.get();
        for (DigestMemo.Entry entry : memo.entries) {
            if (entry != null && entry.owner.get() == this) return entry.digest;
        }
        String digest = SecurityWarrantCanonicalizer.digest(this);
        memo.entries[memo.next] = new DigestMemo.Entry(this, digest);
        memo.next = (memo.next + 1) % memo.entries.length;
        return digest;
    }

    private static final ThreadLocal<DigestMemo> DIGEST_MEMO = ThreadLocal.withInitial(DigestMemo::new);

    /** Small per-thread, identity-keyed memo of recently computed digests. Holds warrants weakly. */
    private static final class DigestMemo {
        final Entry[] entries = new Entry[4];
        int next;

        static final class Entry {
            final java.lang.ref.WeakReference<SecurityWarrant> owner;
            final String digest;

            Entry(SecurityWarrant owner, String digest) {
                this.owner = new java.lang.ref.WeakReference<>(owner);
                this.digest = digest;
            }
        }
    }

    public boolean isTimeValid(Instant now) {
        return !now.isBefore(issuedAt) && now.isBefore(expiresAt);
    }

    public byte[] signature() {
        return signature.clone();
    }

    public SecurityWarrant withSignature(byte[] s) {
        return new SecurityWarrant(
                id,
                issuer,
                subject,
                audience,
                grants,
                constraints,
                issuedAt,
                expiresAt,
                nonce,
                keyId,
                parentId,
                s,
                parentDigest,
                delegationPath);
    }
}
