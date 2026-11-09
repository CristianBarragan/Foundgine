package com.foundgine.providers.gateway.events;

import com.foundgine.providers.gateway.FoundgineGatewayEvent;
import java.time.Duration;
import java.util.ArrayDeque;
import java.util.List;
import java.util.Optional;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.locks.Condition;
import java.util.concurrent.locks.ReentrantLock;

/**
 * Pull-based, gap-free event subscription: replays journal events after a cursor (paged), then follows live events.
 * The hub registers the subscription <em>before</em> any history is read, so nothing published during replay is lost;
 * duplicates are removed by sequence. The live buffer is bounded and drops the <em>oldest</em> entry when full; any
 * hole this leaves is back-filled from the journal before the next live event is delivered.
 * Instances are single-consumer; call {@link #close()} when done.
 */
public final class FoundgineGatewaySubscription implements AutoCloseable {
    private final IFoundgineGatewayEventStore store;
    private final int capacity;
    private final int pageSize;
    private final Runnable onClose;
    private final ReentrantLock lock = new ReentrantLock();
    private final Condition arrived = lock.newCondition();
    private final ArrayDeque<FoundgineGatewayEvent> live = new ArrayDeque<>();
    private final ArrayDeque<FoundgineGatewayEvent> ready = new ArrayDeque<>();
    private boolean closed;
    private boolean replaying = true;
    private boolean started;
    private long lastDelivered;

    FoundgineGatewaySubscription(IFoundgineGatewayEventStore store, int capacity, int pageSize, long after, Runnable onClose) {
        this.store = store; this.capacity = capacity; this.pageSize = pageSize;
        this.lastDelivered = after; this.onClose = onClose;
    }

    /** Called by the hub for every published event (never blocks the publisher). */
    boolean offer(FoundgineGatewayEvent event) {
        lock.lock();
        try {
            if (closed) return false;
            if (live.size() >= capacity) live.pollFirst();
            live.addLast(event);
            arrived.signalAll();
            return true;
        } finally { lock.unlock(); }
    }

    /** Returns the next event, waiting up to {@code timeout}; empty on timeout or after close. */
    public Optional<FoundgineGatewayEvent> poll(Duration timeout) throws InterruptedException {
        long nanos = timeout.toNanos();
        lock.lockInterruptibly();
        try {
            if (!started) {
                started = true;
                var latest = store.getLatestSequence();
                // A cursor from a previous process-local journal can exceed the new high-water mark: restart at 0.
                if (lastDelivered > latest) lastDelivered = 0;
            }
            while (true) {
                if (closed) return Optional.empty();
                var next = ready.pollFirst();
                if (next != null) return Optional.of(next);
                if (replaying) { replayPage(); continue; }
                var head = live.pollFirst();
                if (head != null) {
                    if (head.sequence() <= lastDelivered) continue;
                    if (head.sequence() > lastDelivered + 1 && backfill(head)) continue;
                    lastDelivered = head.sequence();
                    return Optional.of(head);
                }
                if (nanos <= 0L) return Optional.empty();
                nanos = arrived.awaitNanos(nanos);
            }
        } finally { lock.unlock(); }
    }

    private void replayPage() {
        List<FoundgineGatewayEvent> page = store.readAfter(lastDelivered, pageSize);
        for (var e : page.stream().sorted(java.util.Comparator.comparingLong(FoundgineGatewayEvent::sequence)).toList()) {
            if (e.sequence() <= lastDelivered) continue;
            lastDelivered = e.sequence();
            ready.addLast(e);
        }
        if (page.size() < pageSize) replaying = false;
    }

    /** Reads the missing range below {@code head} from the journal; re-queues head. Returns true if progress was made. */
    private boolean backfill(FoundgineGatewayEvent head) {
        var fill = store.readAfter(lastDelivered, pageSize);
        boolean progressed = false;
        for (var e : fill.stream().sorted(java.util.Comparator.comparingLong(FoundgineGatewayEvent::sequence)).toList()) {
            if (e.sequence() <= lastDelivered) continue;
            if (e.sequence() >= head.sequence()) break;
            lastDelivered = e.sequence();
            ready.addLast(e);
            progressed = true;
        }
        if (progressed) { live.addFirst(head); return true; }
        return false; // intentional gap or retention: continue with the live event
    }

    @Override public void close() {
        lock.lock();
        try { closed = true; live.clear(); ready.clear(); arrived.signalAll(); } finally { lock.unlock(); }
        onClose.run();
    }
}
