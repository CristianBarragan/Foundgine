package com.foundgine.providers.security.redis;

import com.foundgine.core.semantic.security.warrants.IBatchedSecurityWarrantRevocationStore;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantRevocationLookup;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantRevocation;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.time.Instant;
import java.util.HexFormat;
import java.util.List;
import java.util.Objects;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicLong;

/** Shared, monotonic warrant revocation state for horizontally scaled Redis/Valkey/Garnet hosts. */
public final class RedisSecurityWarrantRevocationStore implements IBatchedSecurityWarrantRevocationStore {
    public static final String DEFAULT_PREFIX = "foundgine:";
    private static final String REVOKE = """
            local existing = redis.call('HGET', KEYS[1], 'sequence')
            if existing then
              local current = redis.call('INCR', KEYS[3])
              redis.call('SET', KEYS[2], existing, 'NX')
              return {redis.call('HGET', KEYS[1], 'warrantId'), redis.call('HGET', KEYS[1], 'warrantDigest'), redis.call('HGET', KEYS[1], 'revokedAt'), existing, tostring(current)}
            end
            local sequence = redis.call('INCR', KEYS[3])
            redis.call('HSET', KEYS[1], 'warrantId', ARGV[1], 'warrantDigest', ARGV[2], 'revokedAt', ARGV[3], 'sequence', sequence)
            redis.call('SET', KEYS[2], sequence, 'NX')
            return {ARGV[1], ARGV[2], ARGV[3], tostring(sequence), tostring(sequence)}
            """;
    private static final String CURRENT = "return {tostring(redis.call('GET', KEYS[1]) or '0')}";
    private static final String EXISTS = "return {tostring(redis.call('EXISTS', KEYS[1]))}";
    // KEYS: [1] sequence, [2] identity, [3] digest, [4..] delegation ancestor digests. One atomic snapshot.
    private static final String LOOKUP = """
            local sequence = redis.call('GET', KEYS[1]) or '0'
            local revoked = '0'
            if redis.call('EXISTS', KEYS[2]) == 1 or redis.call('EXISTS', KEYS[3]) == 1 then revoked = '1' end
            local ancestor = '0'
            for i = 4, #KEYS do
              if redis.call('EXISTS', KEYS[i]) == 1 then ancestor = '1' break end
            end
            return {tostring(sequence), revoked, ancestor}
            """;
    private static final String HEALTH_PROBE = """
            local sequence = redis.call('GET', KEYS[1]) or '0'
            local written = redis.call('SET', KEYS[2], ARGV[1], 'NX', 'PX', 5000)
            if not written then return {'0', sequence} end
            local observed = redis.call('GET', KEYS[2])
            redis.call('DEL', KEYS[2])
            if observed ~= ARGV[1] then return {'0', sequence} end
            return {'1', sequence}
            """;

    private final RevocationBackend backend;
    private final String keyPrefix;
    private final AtomicLong lastObservedSequence = new AtomicLong();

    public RedisSecurityWarrantRevocationStore(RevocationBackend backend) { this(backend, DEFAULT_PREFIX); }

    public RedisSecurityWarrantRevocationStore(RevocationBackend backend, String keyPrefix) {
        this.backend = Objects.requireNonNull(backend, "backend");
        if (keyPrefix == null || keyPrefix.isBlank() || keyPrefix.indexOf('{') >= 0 || keyPrefix.indexOf('}') >= 0)
            throw new IllegalArgumentException("keyPrefix must be non-blank and must not contain Redis Cluster hash-tag braces.");
        this.keyPrefix = keyPrefix.replaceAll(":+$", "") + ":{warrant-revocations}:";
    }

    @Override public long currentSequence() {
        long floor = lastObservedSequence.get();
        try { return observe(parseOne(backend.eval(CURRENT, List.of(sequenceKey()), List.of()), "current revocation sequence"), floor); }
        catch (RuntimeException e) { throw unavailable(e); }
    }

    @Override public SecurityWarrantRevocation revoke(SecurityWarrant warrant, Instant now) {
        Objects.requireNonNull(warrant, "warrant"); Objects.requireNonNull(now, "now");
        if (warrant.id().isBlank() || warrant.digest().isBlank()) throw new IllegalArgumentException("Warrant identity and digest are required.");
        long floor = lastObservedSequence.get();
        try {
            List<String> result = backend.eval(REVOKE,
                    List.of(identityKey(warrant.id(), warrant.digest()), digestKey(warrant.digest()), sequenceKey()),
                    List.of(warrant.id(), warrant.digest(), Long.toString(now.toEpochMilli())));
            if (result.size() != 5 || result.get(0).isBlank() || result.get(1).isBlank())
                throw new IllegalStateException("Redis returned an invalid warrant revocation record.");
            long revokedAt = parse(result.get(2), "revocation timestamp");
            long sequence = parse(result.get(3), "revocation sequence");
            long current = parse(result.get(4), "current revocation sequence");
            if (sequence <= 0 || current < sequence) throw new IllegalStateException("Redis returned an invalid warrant revocation sequence.");
            observe(current, floor);
            return new SecurityWarrantRevocation(result.get(0), result.get(1), Instant.ofEpochMilli(revokedAt), sequence);
        } catch (RuntimeException e) { throw unavailable(e); }
    }

    @Override public boolean isRevoked(String id, String digest) {
        if (id == null || id.isBlank() || digest == null || digest.isBlank()) throw new IllegalArgumentException("warrant id and digest are required.");
        return exists(identityKey(id, digest));
    }
    @Override public boolean isDigestRevoked(String digest) {
        if (digest == null || digest.isBlank()) throw new IllegalArgumentException("digest is required.");
        return exists(digestKey(digest));
    }

    /** Bounded Redis primary read/write/read/delete round trip for readiness checks. */
    public long probe() {
        String key = keyPrefix + "health:" + UUID.randomUUID().toString().replace("-", "");
        String token = UUID.randomUUID().toString().replace("-", "");
        long floor = lastObservedSequence.get();
        try {
            List<String> result = backend.eval(HEALTH_PROBE, List.of(sequenceKey(), key), List.of(token));
            if (result.size() != 2 || !"1".equals(result.get(0))) throw new IllegalStateException("Redis warrant revocation readiness probe failed its write/read verification.");
            return observe(parse(result.get(1), "sequence during readiness probe"), floor);
        } catch (RuntimeException e) { throw unavailable(e); }
    }

    @Override public SecurityWarrantRevocationLookup lookup(String id, String digest, List<String> ancestorDigests) {
        if (id == null || id.isBlank() || digest == null || digest.isBlank()) throw new IllegalArgumentException("warrant id and digest are required.");
        Objects.requireNonNull(ancestorDigests, "ancestorDigests");
        java.util.ArrayList<String> keys = new java.util.ArrayList<>(3 + ancestorDigests.size());
        keys.add(sequenceKey()); keys.add(identityKey(id, digest)); keys.add(digestKey(digest));
        for (String ancestor : ancestorDigests) keys.add(digestKey(ancestor));
        long floor = lastObservedSequence.get();
        try {
            List<String> result = backend.eval(LOOKUP, keys, List.of());
            if (result.size() != 3 || !isFlag(result.get(1)) || !isFlag(result.get(2)))
                throw new IllegalStateException("Redis returned an invalid warrant revocation lookup result.");
            long sequence = parse(result.get(0), "revocation lookup sequence");
            return new SecurityWarrantRevocationLookup(observe(sequence, floor), "1".equals(result.get(1)), "1".equals(result.get(2)));
        } catch (RuntimeException e) { throw unavailable(e); }
    }
    private static boolean isFlag(String v) { return "0".equals(v) || "1".equals(v); }

    private boolean exists(String key) {
        try {
            List<String> result = backend.eval(EXISTS, List.of(key), List.of());
            return parseOne(result, "revocation existence result") == 1;
        } catch (RuntimeException e) { throw unavailable(e); }
    }
    /**
     * Records a sequence returned by Redis. {@code floor} is the highest sequence this store had already observed
     * <em>before the request was sent</em>. Redis executes the request after everything observed by then, so a reply
     * below the floor means the primary's state rolled back. A reply below a value observed <em>after</em> the request
     * was sent is not a rollback: concurrent calls can complete out of order, and that must not fail closed.
     */
    private long observe(long value, long floor) {
        if (value < floor) throw new IllegalStateException("Warrant revocation sequence regressed from " + floor + " to " + value + "; failing closed after possible Redis state rollback.");
        while (true) {
            long previous = lastObservedSequence.get();
            if (value <= previous || lastObservedSequence.compareAndSet(previous, value)) return value;
        }
    }
    private String sequenceKey() { return keyPrefix + "sequence"; }
    private String identityKey(String id, String digest) { return keyPrefix + "warrant:" + hash(id + "\u001f" + digest); }
    private String digestKey(String digest) { return keyPrefix + "digest:" + hash(digest); }
    private static String hash(String value) {
        try { return HexFormat.of().withUpperCase().formatHex(MessageDigest.getInstance("SHA-256").digest(value.getBytes(StandardCharsets.UTF_8))); }
        catch (NoSuchAlgorithmException e) { throw new IllegalStateException(e); }
    }
    private static long parseOne(List<String> values, String description) {
        if (values.size() != 1) throw new IllegalStateException("Redis returned an invalid " + description + ".");
        return parse(values.get(0), description);
    }
    private static long parse(String value, String description) {
        try { long parsed = Long.parseLong(value); if (parsed < 0) throw new NumberFormatException(); return parsed; }
        catch (RuntimeException e) { throw new IllegalStateException("Redis returned an invalid " + description + ".", e); }
    }
    private static IllegalStateException unavailable(RuntimeException e) {
        return e instanceof IllegalStateException && e.getMessage() != null && e.getMessage().contains("invalid")
                ? (IllegalStateException)e : new IllegalStateException("Warrant revocation store unavailable; failing closed.", e);
    }
}
