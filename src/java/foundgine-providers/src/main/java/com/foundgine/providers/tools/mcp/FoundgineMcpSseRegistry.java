package com.foundgine.providers.tools.mcp;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.function.Predicate;

/**
 * The SSE replay buffers of all sessions, one per session id. Obtain a buffer only for a session the
 * {@link FoundgineMcpSessionGate} has admitted. Bounded: at {@code maxStreams} the least-recently-used buffer is dropped, so
 * an abandoned session can never pin memory; its client then sees an invalid or evicted resume and resynchronises. Call
 * Whenever a buffer leaves the registry (evicted, {@link #remove}d or {@link #sweep}t) it is
 * {@linkplain FoundgineMcpSseBuffer#retire() retired}, which closes any live stream attached to it. Call {@link #remove} when a
 * session is terminated and {@link #sweep} periodically so expired sessions do not keep buffers (the session store is keyed by
 * digest, so only a liveness check can tell). In-process only; a cluster needs its own shared log with the same
 * semantics. Thread-safe.
 */
public final class FoundgineMcpSseRegistry {
    private final int maxStreams;
    private final int maxEvents;
    private final long maxBytes;
    private final Map<String, FoundgineMcpSseBuffer> streams = new LinkedHashMap<>(16, 0.75f, true);

    public FoundgineMcpSseRegistry() { this(10_000, 256, 1_048_576); }

    public FoundgineMcpSseRegistry(int maxStreams, int maxEvents, long maxBytes) {
        if (maxStreams <= 0) throw new IllegalArgumentException("Maximum streams must be positive.");
        new FoundgineMcpSseBuffer(maxEvents, maxBytes);   // validates the bounds
        this.maxStreams = maxStreams;
        this.maxEvents = maxEvents;
        this.maxBytes = maxBytes;
    }

    /** The session's buffer, created on first use. Only call this for an admitted, well-formed session id. */
    public FoundgineMcpSseBuffer stream(String sessionId) {
        if (!FoundgineMcpSessionManager.isWellFormed(sessionId)) throw new IllegalArgumentException("Not a session id.");
        FoundgineMcpSseBuffer evicted = null;
        FoundgineMcpSseBuffer result;
        synchronized (this) {
            FoundgineMcpSseBuffer existing = streams.get(sessionId);   // access-ordered: a hit marks it most recently used
            if (existing != null) return existing;
            if (streams.size() >= maxStreams) {
                var it = streams.entrySet().iterator();
                evicted = it.next().getValue();
                it.remove();
            }
            result = new FoundgineMcpSseBuffer(maxEvents, maxBytes);
            streams.put(sessionId, result);
        }
        if (evicted != null) evicted.retire();   // outside the lock: it runs the stream's close
        return result;
    }

    /** The session's buffer if one exists (never creates one), or {@code null}. Marks it recently used. */
    public synchronized FoundgineMcpSseBuffer find(String sessionId) { return sessionId == null ? null : streams.get(sessionId); }

    /** Drops a session's buffer (call on termination). @return whether one existed */
    public boolean remove(String sessionId) {
        FoundgineMcpSseBuffer removed;
        synchronized (this) { removed = streams.remove(sessionId); }
        if (removed == null) return false;
        removed.retire();
        return true;
    }

    /**
     * Drops the buffers of sessions that are no longer live (e.g. {@code manager::isLive}). The check runs outside the
     * registry's lock, because it may call a shared store.
     *
     * @return how many buffers were dropped
     */
    public int sweep(Predicate<String> isLive) {
        List<String> ids;
        synchronized (this) { ids = new ArrayList<>(streams.keySet()); }
        int dropped = 0;
        for (String id : ids) if (!isLive.test(id) && remove(id)) dropped++;
        return dropped;
    }

    public synchronized int count() { return streams.size(); }
}
