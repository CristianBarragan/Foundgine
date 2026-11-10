package com.foundgine.runtime.controlplane.auditlog;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicLong;

/**
 * Bounded, single-writer audit handoff. record returns only after the durable sink acknowledges
 * persistence. A full queue blocks producers; events are never silently dropped. query is a
 * process-local view of events acknowledged during this process lifetime.
 */
public final class BufferedDurableAuditLog implements IAuditLog, AutoCloseable {
    public record Metrics(int queueDepth, long accepted, long persisted, long failed,
            long backpressureWaits, long backpressureNanos) {}
    private record Pending(AuditEvent event, CompletableFuture<Void> acknowledged, boolean poison) {}

    private final AuditEventStore store;
    private final ArrayBlockingQueue<Pending> queue;
    private final List<AuditEvent> committed = new ArrayList<>();
    private final Object snapshotGate = new Object();
    private final Object enqueueGate = new Object();
    private final AtomicBoolean closed = new AtomicBoolean();
    private final Thread worker;
    private final AtomicLong accepted = new AtomicLong();
    private final AtomicLong persisted = new AtomicLong();
    private final AtomicLong failed = new AtomicLong();
    private final AtomicLong backpressureWaits = new AtomicLong();
    private final AtomicLong backpressureNanos = new AtomicLong();

    /** Returns point-in-time counters; queue depth is approximate under concurrency. */
    public Metrics metrics() {
        return new Metrics(queue.size(), accepted.get(), persisted.get(), failed.get(),
                backpressureWaits.get(), backpressureNanos.get());
    }

    public BufferedDurableAuditLog(AuditEventStore store, int capacity) {
        this.store = Objects.requireNonNull(store, "store");
        if (capacity <= 0) throw new IllegalArgumentException("capacity must be positive");
        this.queue = new ArrayBlockingQueue<>(capacity);
        this.worker = new Thread(this::processQueue, "foundgine-audit-writer");
        this.worker.setDaemon(true);
        this.worker.start();
    }

    @Override
    public void record(AuditEvent event) {
        Objects.requireNonNull(event, "event");
        if (closed.get()) throw new IllegalStateException("audit log is closed");
        var ack = new CompletableFuture<Void>();
        try {
            synchronized (enqueueGate) {
                if (closed.get()) throw new IllegalStateException("audit log is closed");
                long started = System.nanoTime();
                boolean wasFull = queue.remainingCapacity() == 0;
                queue.put(new Pending(event, ack, false));
                long elapsed = System.nanoTime() - started;
                if (wasFull || elapsed >= TimeUnit.MILLISECONDS.toNanos(1)) {
                    backpressureWaits.incrementAndGet(); backpressureNanos.addAndGet(elapsed);
                }
                accepted.incrementAndGet();
            }
            ack.get();
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("interrupted while awaiting durable audit acknowledgement", exception);
        } catch (ExecutionException exception) {
            throw new IllegalStateException("durable audit append failed", exception.getCause());
        }
    }

    @Override
    public List<AuditEvent> query(String toolName, String tenant) {
        synchronized (snapshotGate) {
            var result = new ArrayList<AuditEvent>();
            for (var event : committed) {
                if (toolName != null && !toolName.isBlank() && !toolName.equals(event.toolName())) continue;
                if (tenant != null && !tenant.isBlank() && !tenant.equals(event.tenant())) continue;
                result.add(event);
            }
            return List.copyOf(result);
        }
    }

    private void processQueue() {
        try {
            while (true) {
                var pending = queue.take();
                if (pending.poison()) return;
                try {
                    store.append(pending.event());
                    synchronized (snapshotGate) { committed.add(pending.event()); }
                    persisted.incrementAndGet();
                    pending.acknowledged().complete(null);
                } catch (Throwable failure) {
                    failed.incrementAndGet();
                    pending.acknowledged().completeExceptionally(failure);
                }
            }
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
        }
    }

    /** Stops accepting writes, drains all accepted events, and waits for the writer to finish. */
    @Override
    public void close() {
        synchronized (enqueueGate) {
            if (!closed.compareAndSet(false, true)) return;
            try {
                queue.put(new Pending(null, new CompletableFuture<>(), true));
            } catch (InterruptedException exception) {
                Thread.currentThread().interrupt();
                throw new IllegalStateException("interrupted while closing durable audit queue", exception);
            }
        }
        try {
            worker.join();
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("interrupted while draining durable audit queue", exception);
        }
    }
}
