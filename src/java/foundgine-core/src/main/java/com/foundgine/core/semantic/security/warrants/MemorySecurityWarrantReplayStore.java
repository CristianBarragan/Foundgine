package com.foundgine.core.semantic.security.warrants;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.Objects;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Per-process replay store. Entries consumed with an expiry are discarded after the warrant expires plus a small grace
 * for clock skew, so the store stays bounded by the warrants issued within one lifetime. Entries consumed without an
 * expiry are kept for the life of the process.
 */
public final class MemorySecurityWarrantReplayStore implements ISecurityWarrantReplayStore {
    /** Extra retention after expiry, covering clock skew between the store and the caller that checked the time. */
    static final Duration RETENTION_GRACE = Duration.ofMinutes(1);

    private static final int PURGE_INTERVAL = 256;

    private final ConcurrentHashMap<String, Long> used = new ConcurrentHashMap<>();
    private final AtomicInteger sincePurge = new AtomicInteger();
    private final Clock clock;

    public MemorySecurityWarrantReplayStore() {
        this(Clock.systemUTC());
    }

    public MemorySecurityWarrantReplayStore(Clock clock) {
        this.clock = Objects.requireNonNull(clock);
    }

    /** Number of retained entries (diagnostics and tests). */
    public int size() {
        return used.size();
    }

    @Override
    public boolean tryConsume(String warrantId, String nonce) {
        return consume(warrantId, nonce, Long.MAX_VALUE);
    }

    @Override
    public boolean tryConsume(String warrantId, String nonce, Instant expiresAt) {
        Objects.requireNonNull(expiresAt);
        return consume(warrantId, nonce, retainUntil(expiresAt));
    }

    private boolean consume(String warrantId, String nonce, long retainUntilMillis) {
        purgeIfDue();
        return used.putIfAbsent(warrantId + "\u001f" + nonce, retainUntilMillis) == null;
    }

    private void purgeIfDue() {
        if (sincePurge.incrementAndGet() < PURGE_INTERVAL) return;
        sincePurge.set(0);
        long now = clock.millis();
        used.entrySet().removeIf(e -> e.getValue() <= now);
    }

    static long retainUntil(Instant expiresAt) {
        try {
            return expiresAt.plus(RETENTION_GRACE).toEpochMilli();
        } catch (ArithmeticException | java.time.DateTimeException e) {
            return Long.MAX_VALUE;
        }
    }
}
