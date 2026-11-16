package com.foundgine.providers.security.redis;

import com.foundgine.core.semantic.security.warrants.ISecurityWarrantReplayStore;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.HexFormat;
import java.util.Objects;

/**
 * Shared warrant replay protection for horizontally scaled hosts. Each consumed warrant is recorded with one atomic
 * {@code SET key 1 NX PX ttl}: the first host to consume a warrant wins and every other attempt, on any host, is
 * rejected. Works with Redis, Valkey and Microsoft Garnet (the Redis-protocol server built on FASTER). FASTER itself is
 * an embedded single-process store and cannot share state between replicas; Garnet is the networked option.
 *
 * <p><b>Failure behaviour: fail closed.</b> If the server is unreachable or errors, {@code tryConsume} throws
 * {@link IllegalStateException} and never returns {@code true}, so the request is denied rather than allowed without
 * replay protection. Run the server with replication if an outage must not stop traffic.
 *
 * <p><b>Retention.</b> With an expiry, an entry lives until the warrant expires plus {@link #DEFAULT_GRACE} for clock
 * skew. Without one (the two-argument overload) it lives for {@code defaultRetention}; set that to at least the longest
 * warrant lifetime you issue.
 *
 * <p><b>Keys</b> are {@code prefix + SHA-256(warrantId 0x1F nonce)} in upper-case hex, the same identity the file store
 * uses, so no raw nonce is stored. Use one prefix per environment when sharing a server.
 */
public final class RedisSecurityWarrantReplayStore implements ISecurityWarrantReplayStore {
    public static final String DEFAULT_KEY_PREFIX = "foundgine:warrant-replay:";
    public static final Duration DEFAULT_GRACE = Duration.ofMinutes(1);
    public static final Duration DEFAULT_RETENTION = Duration.ofHours(1);

    /** Redis rejects a PX that overflows; ten years is far beyond any real warrant lifetime. */
    private static final Duration MAX_TTL = Duration.ofDays(3650);

    private final ReplayBackend backend;
    private final String keyPrefix;
    private final Duration defaultRetention;
    private final Duration grace;
    private final Clock clock;

    public RedisSecurityWarrantReplayStore(ReplayBackend backend) {
        this(backend, DEFAULT_KEY_PREFIX, DEFAULT_RETENTION, DEFAULT_GRACE, Clock.systemUTC());
    }

    public RedisSecurityWarrantReplayStore(
            ReplayBackend backend, String keyPrefix, Duration defaultRetention, Duration grace, Clock clock) {
        this.backend = Objects.requireNonNull(backend, "backend");
        this.keyPrefix = Objects.requireNonNull(keyPrefix, "keyPrefix");
        this.defaultRetention = requirePositive(defaultRetention, "defaultRetention");
        this.grace = requirePositive(grace, "grace");
        this.clock = Objects.requireNonNull(clock, "clock");
    }

    @Override
    public boolean tryConsume(String warrantId, String nonce) {
        return consume(warrantId, nonce, defaultRetention);
    }

    @Override
    public boolean tryConsume(String warrantId, String nonce, Instant expiresAt) {
        Objects.requireNonNull(expiresAt, "expiresAt");
        return consume(warrantId, nonce, ttlFor(expiresAt));
    }

    private Duration ttlFor(Instant expiresAt) {
        Duration remaining;
        try {
            remaining = Duration.between(clock.instant(), expiresAt);
        } catch (ArithmeticException e) {
            return MAX_TTL;
        }
        // Already expired by this host's clock: the time check rejects it, but keep a short entry for the skew window.
        Duration ttl = remaining.isNegative() ? grace : remaining.plus(grace);
        return ttl.compareTo(MAX_TTL) > 0 ? MAX_TTL : ttl;
    }

    private boolean consume(String warrantId, String nonce, Duration ttl) {
        if (warrantId == null || warrantId.isBlank() || nonce == null || nonce.isBlank())
            throw new IllegalArgumentException("warrantId and nonce are required.");
        try {
            return backend.setIfAbsent(keyPrefix + identity(warrantId, nonce), ttl);
        } catch (RuntimeException e) {
            throw new IllegalStateException("Warrant replay store unavailable; failing closed.", e);
        }
    }

    private static String identity(String warrantId, String nonce) {
        try {
            return HexFormat.of()
                    .withUpperCase()
                    .formatHex(MessageDigest.getInstance("SHA-256")
                            .digest((warrantId + "\u001f" + nonce).getBytes(StandardCharsets.UTF_8)));
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

    private static Duration requirePositive(Duration d, String name) {
        Objects.requireNonNull(d, name);
        if (d.isZero() || d.isNegative()) throw new IllegalArgumentException(name + " must be positive.");
        return d;
    }
}
