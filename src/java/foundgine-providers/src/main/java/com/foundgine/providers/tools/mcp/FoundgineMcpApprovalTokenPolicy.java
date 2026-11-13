package com.foundgine.providers.tools.mcp;

import java.time.Duration;
import java.util.Objects;
import java.util.function.LongSupplier;

/** Host-configured lifetime and atomic single-use policy for authenticated MCP approval tokens. */
public final class FoundgineMcpApprovalTokenPolicy {
    @FunctionalInterface public interface ReplayStore {
        /** Atomically records an approval id until expiry; returns false if it was already consumed. */
        boolean tryConsume(String approvalId, long expiresAtUnixMilliseconds);
    }

    private final long maximumAgeMillis;
    private final long allowedFutureSkewMillis;
    private final LongSupplier clock;
    private final ReplayStore replayStore;

    public FoundgineMcpApprovalTokenPolicy(Duration maximumAge, Duration allowedFutureSkew,
                                          LongSupplier clock, ReplayStore replayStore) {
        Objects.requireNonNull(maximumAge); Objects.requireNonNull(allowedFutureSkew);
        if (maximumAge.isZero() || maximumAge.isNegative()) throw new IllegalArgumentException("Maximum token age must be positive.");
        if (allowedFutureSkew.isNegative()) throw new IllegalArgumentException("Future clock skew cannot be negative.");
        this.maximumAgeMillis = maximumAge.toMillis();
        this.allowedFutureSkewMillis = allowedFutureSkew.toMillis();
        if (maximumAgeMillis <= 0) throw new IllegalArgumentException("Maximum token age must be at least one millisecond.");
        this.clock = Objects.requireNonNull(clock);
        this.replayStore = Objects.requireNonNull(replayStore);
    }

    public static FoundgineMcpApprovalTokenPolicy defaults(ReplayStore replayStore) {
        return new FoundgineMcpApprovalTokenPolicy(Duration.ofMinutes(5), Duration.ofSeconds(30),
                System::currentTimeMillis, replayStore);
    }

    /** Must be called only after signature verification and before provider execution. */
    public void validateAndConsume(FoundgineMcpApprovalToken.Claims claims) {
        Objects.requireNonNull(claims);
        long now = clock.getAsLong();
        long approvedAt = claims.approvedAtUnixMilliseconds();
        if (approvedAt > saturatingAdd(now, allowedFutureSkewMillis))
            throw new SecurityException("MCP approval token is not yet valid.");
        long expiresAt = saturatingAdd(approvedAt, maximumAgeMillis);
        if (now >= expiresAt)
            throw new SecurityException("MCP approval token has expired.");
        if (!replayStore.tryConsume(claims.approvalId(), expiresAt))
            throw new SecurityException("MCP approval token has already been consumed.");
    }

    private static long saturatingAdd(long a, long b) {
        try { return Math.addExact(a, b); }
        catch (ArithmeticException ignored) { return b >= 0 ? Long.MAX_VALUE : Long.MIN_VALUE; }
    }
}
