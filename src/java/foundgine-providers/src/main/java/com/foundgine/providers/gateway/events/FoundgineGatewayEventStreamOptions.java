package com.foundgine.providers.gateway.events;

import java.time.Duration;
import java.util.Arrays;

/** Configuration of the gateway event stream (heartbeat, replay window, audience rules). */
public final class FoundgineGatewayEventStreamOptions {
    private byte[] scopeDigestKey;
    private Duration heartbeatInterval = Duration.ofSeconds(15);
    private Duration reconnectDelay = Duration.ofSeconds(3);
    private long maxReplayWindow = 10_000;
    private String operatorPolicy;

    public Duration heartbeatInterval() { return heartbeatInterval; }
    public FoundgineGatewayEventStreamOptions heartbeatInterval(Duration v) { heartbeatInterval = v; return this; }
    public Duration reconnectDelay() { return reconnectDelay; }
    public FoundgineGatewayEventStreamOptions reconnectDelay(Duration v) { reconnectDelay = v; return this; }
    /** Largest number of sequence values a reconnecting client may lag behind before the stream answers "too old". */
    public long maxReplayWindow() { return maxReplayWindow; }
    public FoundgineGatewayEventStreamOptions maxReplayWindow(long v) { maxReplayWindow = v; return this; }
    /** Authorization policy whose callers may see unscoped system events. {@code null} (default): nobody. */
    public String operatorPolicy() { return operatorPolicy; }
    public FoundgineGatewayEventStreamOptions operatorPolicy(String v) { operatorPolicy = v; return this; }

    /** Defensive copy of the HMAC key, or null when unset. */
    public byte[] scopeDigestKey() { return scopeDigestKey == null ? null : scopeDigestKey.clone(); }

    /** Sets the HMAC key (at least 32 bytes) for scope digests. Must be identical on all replicas. */
    public FoundgineGatewayEventStreamOptions setScopeDigestKey(byte[] key) {
        if (key == null || key.length < 32) throw new IllegalArgumentException("The scope digest key must be at least 32 bytes.");
        scopeDigestKey = Arrays.copyOf(key, key.length);
        return this;
    }

    public void validate() {
        if (heartbeatInterval == null || heartbeatInterval.isZero() || heartbeatInterval.isNegative())
            throw new IllegalArgumentException("heartbeatInterval must be positive");
        if (reconnectDelay == null || reconnectDelay.isNegative())
            throw new IllegalArgumentException("reconnectDelay must not be negative");
        if (maxReplayWindow < 1) throw new IllegalArgumentException("maxReplayWindow must be at least 1");
    }
}
