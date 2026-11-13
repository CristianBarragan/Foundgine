package com.foundgine.providers.tools.mcp;

import java.security.GeneralSecurityException;
import java.security.PrivateKey;
import java.security.PublicKey;
import java.security.interfaces.RSAPrivateKey;
import java.security.interfaces.RSAPublicKey;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Set;
import java.util.function.LongSupplier;

/**
 * Rotating key set for {@code fgmcp1} approval tokens. It is both the {@link FoundgineMcpApprovalToken.Signer} (it signs
 * with the single <em>active signing key</em>) and the {@link FoundgineMcpApprovalToken.KeyResolver} (it verifies with the
 * active key and with every retained public key that has not passed its deadline).
 *
 * <p><b>Rotation.</b> {@link #activateSigningKey(String, PrivateKey, PublicKey, Duration)} installs a new signing key and
 * demotes the previous one to verify-only for {@code retainPreviousFor}, so approvals that are still outstanding keep
 * verifying. The retention must be at least {@code minimumRetention}; size it with
 * {@link FoundgineMcpApprovalTokenPolicy#minimumKeyRetention()} (maximum token age plus future skew). After the deadline the
 * old key stops verifying, which is also the moment the policy would have rejected its last token anyway. Hosts that only
 * verify (other instances, other services) learn about rotations with {@link #addVerificationKey} and {@link #retireAfter}.
 *
 * <p><b>Rules.</b> A key id is never reused: it stays reserved after expiry or revocation, so a token can never be
 * re-validated by new key material under an old id. {@link #revoke} removes a key immediately, regardless of any
 * retention, and is the response to a suspected compromise. Verification never has side effects, and unknown, expired and
 * revoked ids are all reported as "no key", so the ring leaks nothing about which of them an attacker hit. RSA keys must be
 * at least {@value #MINIMUM_RSA_KEY_BITS} bits. Signing uses RSA PKCS#1 v1.5 / SHA-256, so existing tokens and the shared
 * golden vector are unchanged.
 *
 * <p>This type does not authenticate requests, hold private keys beyond the active one, or replace expiry and replay
 * policy ({@link FoundgineMcpApprovalTokenPolicy}). Private-key custody remains the host's job. Thread-safe: readers are
 * lock-free over an immutable snapshot, mutations are serialized.
 */
public final class FoundgineMcpApprovalKeyRing
        implements FoundgineMcpApprovalToken.Signer, FoundgineMcpApprovalToken.KeyResolver {
    public static final int MINIMUM_RSA_KEY_BITS = 2048;
    private static final long NO_DEADLINE = Long.MAX_VALUE;

    public enum Role { SIGNING, VERIFY_ONLY }

    /**
     * A currently usable key. {@code verifiableUntilUnixMilliseconds} is {@code null} when the key has no deadline.
     */
    public record KeyInfo(String keyId, Role role, Long verifiableUntilUnixMilliseconds) {}

    /** The private key is present only for the active signing key. */
    private record Entry(RSAPublicKey publicKey, RSAPrivateKey privateKey, long verifiableUntil) {}

    private record State(String signingKeyId, Map<String, Entry> keys, Set<String> revoked) {}

    private final Duration minimumRetention;
    private final LongSupplier clock;
    private final FoundgineMcpAuditSink audit;
    private volatile State state = new State(null, Map.of(), Set.of());

    /** No minimum retention is enforced; prefer {@link #FoundgineMcpApprovalKeyRing(Duration)}. */
    public FoundgineMcpApprovalKeyRing() { this(Duration.ZERO, System::currentTimeMillis); }

    public FoundgineMcpApprovalKeyRing(Duration minimumRetention) { this(minimumRetention, System::currentTimeMillis); }

    public FoundgineMcpApprovalKeyRing(Duration minimumRetention, LongSupplier clock) {
        this(minimumRetention, clock, FoundgineMcpAuditSink.NONE);
    }

    /**
     * Emits {@link FoundgineMcpAuditEvent}s for activation, rotation, added verification keys, retirement and revocation. Events
     * are emitted after the change has taken effect and never carry key material.
     */
    public FoundgineMcpApprovalKeyRing(Duration minimumRetention, LongSupplier clock, FoundgineMcpAuditSink audit) {
        this.audit = Objects.requireNonNull(audit, "audit");
        Objects.requireNonNull(minimumRetention, "minimumRetention");
        if (minimumRetention.isNegative()) throw new IllegalArgumentException("Minimum retention cannot be negative.");
        this.minimumRetention = minimumRetention;
        this.clock = Objects.requireNonNull(clock, "clock");
    }

    /** A ring whose minimum retention is the policy's token age plus skew. */
    public static FoundgineMcpApprovalKeyRing forPolicy(FoundgineMcpApprovalTokenPolicy policy) {
        return new FoundgineMcpApprovalKeyRing(Objects.requireNonNull(policy, "policy").minimumKeyRetention());
    }

    private void emit(FoundgineMcpAuditEvent.Type type, String keyId, String detail) {
        FoundgineMcpAuditSink.emit(audit, new FoundgineMcpAuditEvent(type, clock.getAsLong(), keyId, null, detail));
    }

    // ---- signing -----------------------------------------------------------------------------------------------------

    /** The id of the active signing key. @throws IllegalStateException when no signing key is active. */
    @Override
    public String keyId() {
        String id = state.signingKeyId();
        if (id == null) throw new IllegalStateException("No active approval-token signing key.");
        return id;
    }

    /** Signs with the active key. Prefer {@link #snapshot()} (which {@code encode} uses) when the key id matters. */
    @Override
    public byte[] sign(byte[] payload) throws GeneralSecurityException {
        return snapshot().sign(payload);
    }

    /** A signer fixed to the key that is active now, so id and signature in one token can never come from different keys. */
    @Override
    public FoundgineMcpApprovalToken.Signer snapshot() {
        State s = state;
        if (s.signingKeyId() == null) throw new IllegalStateException("No active approval-token signing key.");
        return FoundgineMcpApprovalToken.rsaSha256Pkcs1Signer(s.signingKeyId(), s.keys().get(s.signingKeyId()).privateKey());
    }

    // ---- verification ------------------------------------------------------------------------------------------------

    /** The public key for {@code keyId}, or {@code null} when it is unknown, revoked or past its deadline. */
    @Override
    public PublicKey resolve(String keyId) {
        if (keyId == null) return null;
        State s = state;
        Entry e = s.keys().get(keyId);
        if (e == null || clock.getAsLong() >= e.verifiableUntil()) return null;
        return e.publicKey();
    }

    /** Usable keys right now: the signing key first, then verify-only keys by id. */
    public List<KeyInfo> keys() {
        State s = state;
        long now = clock.getAsLong();
        List<KeyInfo> out = new ArrayList<>();
        s.keys().forEach((id, e) -> {
            if (now >= e.verifiableUntil()) return;
            boolean signing = id.equals(s.signingKeyId());
            out.add(new KeyInfo(id, signing ? Role.SIGNING : Role.VERIFY_ONLY,
                    e.verifiableUntil() == NO_DEADLINE ? null : e.verifiableUntil()));
        });
        out.sort((a, b) -> a.role() != b.role() ? a.role().compareTo(b.role()) : a.keyId().compareTo(b.keyId()));
        return List.copyOf(out);
    }

    // ---- rotation ----------------------------------------------------------------------------------------------------

    /**
     * Installs the first signing key (or the first after {@link #revoke} removed the previous one). Use the four-argument
     * overload to replace an active key.
     */
    public synchronized void activateSigningKey(String keyId, PrivateKey privateKey, PublicKey publicKey) {
        if (state.signingKeyId() != null)
            throw new IllegalStateException(
                    "A signing key is already active; pass the retention for the previous key to rotate.");
        install(keyId, privateKey, publicKey, null);
        emit(FoundgineMcpAuditEvent.Type.KEY_ACTIVATED, keyId, null);
    }

    /**
     * Rotates: {@code keyId} becomes the signing key and the previous signing key stays verifiable for
     * {@code retainPreviousFor} (at least the configured minimum, and positive). Nothing changes if validation fails.
     */
    public synchronized void activateSigningKey(String keyId, PrivateKey privateKey, PublicKey publicKey,
                                                Duration retainPreviousFor) {
        Objects.requireNonNull(retainPreviousFor, "retainPreviousFor");
        if (state.signingKeyId() == null) {
            install(keyId, privateKey, publicKey, null);
            emit(FoundgineMcpAuditEvent.Type.KEY_ACTIVATED, keyId, null);
            return;
        }
        String previous = state.signingKeyId();
        install(keyId, privateKey, publicKey, requireRetention(retainPreviousFor));
        emit(FoundgineMcpAuditEvent.Type.KEY_ROTATED, keyId, "previous=" + previous + " retainedFor=" + retainPreviousFor);
    }

    /**
     * Trusts a public key for verification only. {@code verifiableUntil} may be {@code null} (no deadline) or must lie at
     * least the configured minimum retention in the future.
     */
    public synchronized void addVerificationKey(String keyId, PublicKey publicKey, Instant verifiableUntil) {
        requireKeyId(keyId);
        RSAPublicKey rsa = requireRsaPublic(publicKey);
        long deadline = NO_DEADLINE;
        if (verifiableUntil != null) {
            deadline = verifiableUntil.toEpochMilli();
            long now = clock.getAsLong();
            if (deadline <= now) throw new IllegalArgumentException("The verification deadline must be in the future.");
            requireRetention(Duration.ofMillis(Math.max(1, deadline - now)));
        }
        State s = state;
        requireUnused(s, keyId);
        Map<String, Entry> keys = new HashMap<>(s.keys());
        keys.put(keyId, new Entry(rsa, null, deadline));
        state = new State(s.signingKeyId(), Map.copyOf(keys), s.revoked());
        emit(FoundgineMcpAuditEvent.Type.KEY_ADDED, keyId, deadline == NO_DEADLINE ? "verifiableUntil=none" : "verifiableUntil=" + deadline);
    }

    /**
     * Schedules a verify-only key to stop verifying after {@code retainFor} (at least the configured minimum). It only ever
     * shortens a deadline; an earlier existing deadline is kept. The signing key cannot be retired: activate its
     * replacement first.
     *
     * @return {@code false} when the key is unknown or already unusable
     */
    public synchronized boolean retireAfter(String keyId, Duration retainFor) {
        requireKeyId(keyId);
        long deadline = saturatingAdd(clock.getAsLong(), requireRetention(retainFor).toMillis());
        State s = state;
        if (keyId.equals(s.signingKeyId()))
            throw new IllegalStateException("The active signing key cannot be retired; activate a replacement first.");
        Entry e = s.keys().get(keyId);
        if (e == null || clock.getAsLong() >= e.verifiableUntil()) return false;
        Map<String, Entry> keys = new HashMap<>(s.keys());
        long effective = Math.min(e.verifiableUntil(), deadline);
        keys.put(keyId, new Entry(e.publicKey(), null, effective));
        state = new State(s.signingKeyId(), Map.copyOf(keys), s.revoked());
        emit(FoundgineMcpAuditEvent.Type.KEY_RETIRED, keyId, "verifiableUntil=" + effective);
        return true;
    }

    /**
     * Removes a key immediately, whatever its retention, and reserves its id forever. Every token signed under it stops
     * verifying. Revoking the signing key leaves the ring unable to sign until a new key is activated. Unknown ids are
     * reserved too, so a revocation can be applied before the key is ever added.
     *
     * @return {@code true} if a key was removed
     */
    public synchronized boolean revoke(String keyId) {
        requireKeyId(keyId);
        State s = state;
        Map<String, Entry> keys = new HashMap<>(s.keys());
        boolean removed = keys.remove(keyId) != null;
        Set<String> revoked = new HashSet<>(s.revoked());
        revoked.add(keyId);
        boolean wasSigning = keyId.equals(s.signingKeyId());
        state = new State(wasSigning ? null : s.signingKeyId(), Map.copyOf(keys), Set.copyOf(revoked));
        emit(FoundgineMcpAuditEvent.Type.KEY_REVOKED, keyId, "removed=" + removed + " wasSigning=" + wasSigning);
        return removed;
    }

    // ---- internals ---------------------------------------------------------------------------------------------------

    private void install(String keyId, PrivateKey privateKey, PublicKey publicKey, Duration retainPrevious) {
        requireKeyId(keyId);
        RSAPublicKey pub = requireRsaPublic(publicKey);
        if (!(privateKey instanceof RSAPrivateKey priv)) throw new IllegalArgumentException("RSA private key required.");
        if (!priv.getModulus().equals(pub.getModulus()))
            throw new IllegalArgumentException("The private and public keys are not a pair.");
        State s = state;
        requireUnused(s, keyId);
        Map<String, Entry> keys = new HashMap<>(s.keys());
        if (retainPrevious != null) {
            Entry old = keys.get(s.signingKeyId());
            long deadline = saturatingAdd(clock.getAsLong(), retainPrevious.toMillis());
            keys.put(s.signingKeyId(), new Entry(old.publicKey(), null, deadline));
        }
        keys.put(keyId, new Entry(pub, priv, NO_DEADLINE));
        state = new State(keyId, Map.copyOf(keys), s.revoked());
    }

    private Duration requireRetention(Duration retain) {
        Objects.requireNonNull(retain, "retain");
        if (retain.isZero() || retain.isNegative()) throw new IllegalArgumentException("Retention must be positive.");
        if (retain.compareTo(minimumRetention) < 0)
            throw new IllegalArgumentException("Retention is shorter than the minimum of " + minimumRetention
                    + "; outstanding approvals signed by the old key would stop verifying early.");
        return retain;
    }

    private static void requireUnused(State s, String keyId) {
        if (s.keys().containsKey(keyId) || s.revoked().contains(keyId))
            throw new IllegalArgumentException("Approval-token key id '" + keyId + "' was already used; key ids are never reused.");
    }

    private static void requireKeyId(String keyId) {
        if (keyId == null || keyId.isBlank()) throw new IllegalArgumentException("A key id is required.");
    }

    private static RSAPublicKey requireRsaPublic(PublicKey key) {
        if (!(key instanceof RSAPublicKey rsa)) throw new IllegalArgumentException("RSA public key required.");
        if (rsa.getModulus().bitLength() < MINIMUM_RSA_KEY_BITS)
            throw new IllegalArgumentException("RSA keys must be at least " + MINIMUM_RSA_KEY_BITS + " bits.");
        return rsa;
    }

    private static long saturatingAdd(long a, long b) {
        try { return Math.addExact(a, b); }
        catch (ArithmeticException ignored) { return Long.MAX_VALUE; }
    }
}
