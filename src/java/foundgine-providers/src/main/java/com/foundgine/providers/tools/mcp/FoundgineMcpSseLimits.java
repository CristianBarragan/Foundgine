package com.foundgine.providers.tools.mcp;

import java.util.HashMap;
import java.util.Map;

/**
 * Caps how many live SSE streams may be open at once: in total and per principal, so one client (or one stolen credential)
 * cannot hold unbounded connections. A principal is the authenticated name the session is bound to; sessions with no owner
 * share one bucket. {@link #tryAcquire} either returns a {@link Permit} that must be closed when the stream ends, or
 * {@code null} when a limit is reached. Thread-safe.
 */
public final class FoundgineMcpSseLimits {
    /** No limits at all. */
    public static final FoundgineMcpSseLimits UNLIMITED = new FoundgineMcpSseLimits(Integer.MAX_VALUE, Integer.MAX_VALUE);

    private static final String UNBOUND = "\u0000unbound";

    /** One open stream's claim; {@link #close} is idempotent. */
    public final class Permit implements AutoCloseable {
        private final String key;
        private boolean released;
        private Permit(String key) { this.key = key; }
        @Override public void close() {
            synchronized (FoundgineMcpSseLimits.this) {
                if (released) return;
                released = true;
                total--;
                perOwner.merge(key, -1, Integer::sum);
                perOwner.remove(key, 0);
            }
        }
    }

    private final int maxTotal;
    private final int maxPerOwner;
    private final Map<String, Integer> perOwner = new HashMap<>();
    private int total;

    public FoundgineMcpSseLimits(int maxTotal, int maxPerOwner) {
        if (maxTotal <= 0) throw new IllegalArgumentException("Maximum open streams must be positive.");
        if (maxPerOwner <= 0) throw new IllegalArgumentException("Maximum streams per principal must be positive.");
        this.maxTotal = maxTotal;
        this.maxPerOwner = maxPerOwner;
    }

    /** @return a permit, or {@code null} when the total or the principal's limit is reached */
    public synchronized Permit tryAcquire(String owner) {
        String key = owner == null ? UNBOUND : owner;
        if (total >= maxTotal || perOwner.getOrDefault(key, 0) >= maxPerOwner) return null;
        total++;
        perOwner.merge(key, 1, Integer::sum);
        return new Permit(key);
    }

    public synchronized int open() { return total; }
}
