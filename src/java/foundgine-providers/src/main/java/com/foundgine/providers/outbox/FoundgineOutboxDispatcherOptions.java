package com.foundgine.providers.outbox;

import java.time.Duration;

public final class FoundgineOutboxDispatcherOptions {
    private int batchSize = 50;
    private Duration pollInterval = Duration.ofSeconds(1);
    private Duration leaseDuration = Duration.ofSeconds(30);
    private int maxAttempts = 8;
    private Duration baseBackoff = Duration.ofSeconds(2);
    private Duration maxBackoff = Duration.ofMinutes(5);

    public int batchSize() { return batchSize; }
    public Duration pollInterval() { return pollInterval; }
    /** Must exceed the worst-case publish time of one batch, or messages are delivered twice. */
    public Duration leaseDuration() { return leaseDuration; }
    public int maxAttempts() { return maxAttempts; }
    public Duration baseBackoff() { return baseBackoff; }
    public Duration maxBackoff() { return maxBackoff; }
    public FoundgineOutboxDispatcherOptions batchSize(int v) { batchSize = v; return this; }
    public FoundgineOutboxDispatcherOptions pollInterval(Duration v) { pollInterval = v; return this; }
    public FoundgineOutboxDispatcherOptions leaseDuration(Duration v) { leaseDuration = v; return this; }
    public FoundgineOutboxDispatcherOptions maxAttempts(int v) { maxAttempts = v; return this; }
    public FoundgineOutboxDispatcherOptions baseBackoff(Duration v) { baseBackoff = v; return this; }
    public FoundgineOutboxDispatcherOptions maxBackoff(Duration v) { maxBackoff = v; return this; }

    public void validate() {
        if (batchSize < 1 || batchSize > 1_000) throw new IllegalArgumentException("batchSize must be 1-1,000");
        if (pollInterval == null || pollInterval.isZero() || pollInterval.isNegative()) throw new IllegalArgumentException("pollInterval must be positive");
        if (leaseDuration == null || leaseDuration.isZero() || leaseDuration.isNegative()) throw new IllegalArgumentException("leaseDuration must be positive");
        if (maxAttempts < 1) throw new IllegalArgumentException("maxAttempts must be at least one");
        if (baseBackoff == null || baseBackoff.isZero() || baseBackoff.isNegative() || maxBackoff == null || maxBackoff.compareTo(baseBackoff) < 0)
            throw new IllegalArgumentException("baseBackoff must be positive and not exceed maxBackoff");
    }
}
