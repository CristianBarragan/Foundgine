package com.foundgine.providers.outbox;

import com.foundgine.providers.gateway.FoundgineGatewayEventType;
import com.foundgine.providers.gateway.FoundgineProviderCapability;
import com.foundgine.providers.gateway.FoundgineProviderKind;
import com.foundgine.providers.gateway.FoundgineProviderRequirement;
import com.foundgine.providers.gateway.IFoundgineGatewayEventPublisher;
import com.foundgine.providers.gateway.diagnostics.FoundgineGatewayDiagnostics;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.LinkedHashMap;

/**
 * Leases due messages, hands them to the publisher, then completes, schedules a retry with exponential backoff, or
 * dead-letters after {@code maxAttempts}. Delivery is at-least-once. Call {@link #start()} to run a background
 * polling thread, or drive {@link #dispatchOnce()} yourself.
 */
public final class FoundgineOutboxDispatcher implements AutoCloseable {
    private final IFoundgineGatewayOutboxStore store;
    private final IFoundgineOutboxPublisher publisher;
    private final FoundgineOutboxDispatcherOptions options;
    private final IFoundgineGatewayEventPublisher events;
    private final Clock clock;
    private volatile Thread worker;
    private volatile boolean stopping;

    public FoundgineOutboxDispatcher(IFoundgineGatewayOutboxStore store, IFoundgineOutboxPublisher publisher,
            FoundgineOutboxDispatcherOptions options) { this(store, publisher, options, null, null); }

    public FoundgineOutboxDispatcher(IFoundgineGatewayOutboxStore store, IFoundgineOutboxPublisher publisher,
            FoundgineOutboxDispatcherOptions options, IFoundgineGatewayEventPublisher events, Clock clock) {
        if (store == null) throw new IllegalArgumentException("store is required");
        if (publisher == null) throw new IllegalArgumentException("publisher is required");
        if (options == null) throw new IllegalArgumentException("options is required");
        options.validate();
        this.store = store;
        this.publisher = publisher;
        this.options = options;
        this.events = events;
        this.clock = clock == null ? Clock.systemUTC() : clock;
    }

    /** Deterministic exponential backoff (no jitter) for the given 1-based attempt, capped at maxBackoff. */
    public static Duration computeBackoff(int attempt, FoundgineOutboxDispatcherOptions options) {
        if (options == null) throw new IllegalArgumentException("options is required");
        int exponent = Math.max(0, Math.min(30, attempt - 1));
        double millis = options.baseBackoff().toMillis() * Math.pow(2, exponent);
        return millis >= options.maxBackoff().toMillis() ? options.maxBackoff() : Duration.ofMillis((long) millis);
    }

    /** Capability requirement a host should negotiate at startup: the dispatcher retries and dead-letters messages. */
    public static FoundgineProviderRequirement requirement() {
        return new FoundgineProviderRequirement(FoundgineProviderKind.OUTBOX,
                FoundgineProviderCapability.mask(FoundgineProviderCapability.RETRY, FoundgineProviderCapability.DEAD_LETTER),
                "The outbox dispatcher retries and dead-letters messages.");
    }

    public synchronized void start() {
        if (worker != null) return;
        stopping = false;
        var t = new Thread(this::run, "foundgine-outbox-dispatcher");
        t.setDaemon(true);
        worker = t;
        t.start();
    }

    @Override public synchronized void close() {
        stopping = true;
        Thread t = worker;
        worker = null;
        if (t == null) return;
        t.interrupt();
        try { t.join(5_000); } catch (InterruptedException e) { Thread.currentThread().interrupt(); }
    }

    private void run() {
        while (!stopping && !Thread.currentThread().isInterrupted()) {
            int processed = 0;
            try {
                processed = dispatchOnce();
            } catch (RuntimeException e) {
                // Store outage or similar: leases expire on their own; back off and retry the loop.
                FoundgineGatewayDiagnostics.recordOutbox("loop_error");
            }
            if (processed > 0) continue; // drain without sleeping
            try {
                Thread.sleep(options.pollInterval().toMillis());
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                break;
            }
        }
    }

    /** Runs one lease/publish/acknowledge cycle and returns the number of messages handled. */
    public int dispatchOnce() {
        var leases = store.lease(options.batchSize(), options.leaseDuration(), clock.instant());
        int handled = 0;
        for (var lease : leases) {
            // Interruption is shutdown/cancellation, not a broker failure. Leave this lease (and the
            // remaining batch) untouched so the lease timeout safely makes them available again.
            if (Thread.currentThread().isInterrupted()) break;

            Throwable failure = null;
            try {
                publisher.publish(lease.message());
            } catch (InterruptedException interrupted) {
                Thread.currentThread().interrupt();
                break;
            } catch (Exception e) {
                failure = e;
            }
            if (failure == null) {
                // A failure here propagates: the published message is redelivered after lease expiry (at-least-once).
                store.complete(lease, clock.instant());
                FoundgineGatewayDiagnostics.recordOutbox("published");
            } else {
                handleFailure(lease, failure);
            }
            handled++;
        }
        return handled;
    }

    private void handleFailure(FoundgineOutboxLease lease, Throwable failure) {
        Instant now = clock.instant();
        String error = failure.getClass().getSimpleName() + ": " + failure.getMessage();
        if (lease.attempt() >= options.maxAttempts()) {
            store.fail(lease, error, null, now);
            FoundgineGatewayDiagnostics.recordOutbox("dead_lettered");
            tryAnnounceDeadLetter(lease);
            return;
        }
        store.fail(lease, error, now.plus(computeBackoff(lease.attempt(), options)), now);
        FoundgineGatewayDiagnostics.recordOutbox("retry_scheduled");
    }

    private void tryAnnounceDeadLetter(FoundgineOutboxLease lease) {
        if (events == null) return;
        try {
            // Operational signal only: ids and topic, never the payload.
            var attributes = new LinkedHashMap<String, String>();
            attributes.put("messageId", lease.message().id().toString());
            attributes.put("topic", lease.message().topic());
            attributes.put("attempts", Integer.toString(lease.attempt()));
            events.publish(FoundgineGatewayEventType.CUSTOM, "outbox.dead_lettered", lease.message().correlationId(), null, attributes);
        } catch (RuntimeException ignored) {
            // The dead-letter state is already durable; announcing it is best effort.
        }
    }
}
