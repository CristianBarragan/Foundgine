package com.foundgine.providers.security.redis;

import com.foundgine.core.semantic.security.warrants.ISecurityWarrantRevocationStore;
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
public final class RedisSecurityWarrantRevocationStore implements ISecurityWarrantRevocationStore {
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
        try { return observe(parseOne(backend.eval(CURRENT, List.of(sequenceKey()), List.of()), "current revocation sequence")); }
        catch (RuntimeException e) { throw unavailable(e); }
    }

    @Override public SecurityWarrantRevocation revoke(SecurityWarrant warrant, Instant now) {
        Objects.requireNonNull(warrant, "warrant"); Objects.requireNonNull(now, "now");
        if (warrant.id().isBlank() || warrant.digest().isBlank()) throw new IllegalArgumentException("Warrant identity and digest are required.");
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
            observe(current);
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
        try {
            List<String> result = backend.eval(HEALTH_PROBE, List.of(sequenceKey(), key), List.of(token));
            if (result.size() != 2 || !"1".equals(result.get(0))) throw new IllegalStateException("Redis warrant revocation readiness probe failed its write/read verification.");
            return observe(parse(result.get(1), "sequence during readiness probe"));
        } catch (RuntimeException e) { throw unavailable(e); }
    }

    private boolean exists(String key) {
        try {
            List<String> result = backend.eval(EXISTS, List.of(key), List.of());
            return parseOne(result, "revocation existence result") == 1;
        } catch (RuntimeException e) { throw unavailable(e); }
    }
    private long observe(long value) {
        long previous = lastObservedSequence.get();
        while (true) {
            if (value < previous) throw new IllegalStateException("Warrant revocation sequence regressed from " + previous + " to " + value + "; failing closed after possible Redis state rollback.");
            if (value == previous || lastObservedSequence.compareAndSet(previous, value)) return value;
            previous = lastObservedSequence.get();
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
