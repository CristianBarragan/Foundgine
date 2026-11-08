package com.foundgine.providers.gateway.events;

import com.foundgine.providers.gateway.FoundgineGatewayEvent;
import com.foundgine.providers.gateway.FoundgineGatewayEventType;
import com.foundgine.providers.gateway.FoundgineGatewayOptions;
import com.foundgine.providers.gateway.IFoundgineGatewayCriticalHook;
import com.foundgine.providers.gateway.IFoundgineGatewayEventPublisher;
import com.foundgine.providers.gateway.IFoundgineGatewayHook;
import com.foundgine.providers.gateway.diagnostics.FoundgineGatewayDiagnostics;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;

/**
 * In-process fan-out for gateway notifications. Replay is backed by the configured journal; the default
 * in-memory journal is process-local and not durable.
 * <p>Publish order: critical hooks (fail-closed) -> journal append -> best-effort hooks -> live subscribers.
 */
public final class FoundgineGatewayEventHub implements IFoundgineGatewayEventPublisher {
    private final ConcurrentHashMap<UUID, FoundgineGatewaySubscription> subscribers = new ConcurrentHashMap<>();
    private final List<IFoundgineGatewayHook> hooks;
    private final int capacity;
    private final int replayPageSize;
    private final IFoundgineGatewayEventStore eventStore;

    public FoundgineGatewayEventHub(FoundgineGatewayOptions options) { this(options, List.of(), null); }

    public FoundgineGatewayEventHub(FoundgineGatewayOptions options, List<? extends IFoundgineGatewayHook> hooks,
            IFoundgineGatewayEventStore eventStore) {
        Objects.requireNonNull(options, "options");
        if (options.subscriberBufferCapacity() < 1) throw new IllegalArgumentException("SubscriberBufferCapacity must be at least one.");
        if (options.replayPageSize() < 1 || options.replayPageSize() > 10_000)
            throw new IllegalArgumentException("ReplayPageSize must be 1-10,000.");
        this.capacity = options.subscriberBufferCapacity();
        this.replayPageSize = options.replayPageSize();
        this.hooks = hooks == null ? List.of() : List.copyOf(hooks);
        this.eventStore = eventStore == null ? new InMemoryFoundgineGatewayEventStore() : eventStore;
    }

    public IFoundgineGatewayEventStore eventStore() { return eventStore; }

    @Override
    public void publish(FoundgineGatewayEventType type, String subject, String correlationId, String providerId,
            Map<String, String> attributes) {
        if (subject == null || subject.isBlank()) throw new IllegalArgumentException("subject is required");
        long started = System.nanoTime();
        var gatewayEvent = FoundgineGatewayEvent.create(type, subject, correlationId, providerId, attributes);

        // Critical hooks (for example mandatory durable audit) must accept the event before anything else can observe it.
        for (var hook : hooks) {
            if (!(hook instanceof IFoundgineGatewayCriticalHook)) continue;
            try {
                hook.onEvent(gatewayEvent);
            } catch (Exception failure) {
                FoundgineGatewayDiagnostics.recordHookFailure(hook.getClass().getSimpleName(), true);
                if (failure instanceof RuntimeException re) throw re;
                throw new IllegalStateException("Critical gateway hook rejected event.", failure);
            }
        }

        gatewayEvent = eventStore.append(gatewayEvent);

        for (var hook : hooks) {
            if (hook instanceof IFoundgineGatewayCriticalHook) continue;
            try {
                hook.onEvent(gatewayEvent);
            } catch (Exception failure) {
                // Telemetry, cache invalidation and broker fan-out hooks are isolated from the request path.
                if (failure instanceof InterruptedException) Thread.currentThread().interrupt();
                FoundgineGatewayDiagnostics.recordHookFailure(hook.getClass().getSimpleName(), false);
            }
        }

        for (var subscriber : subscribers.values())
            if (subscriber.offer(gatewayEvent)) FoundgineGatewayDiagnostics.recordEnqueued(type);

        FoundgineGatewayDiagnostics.recordPublished(type, providerId, System.nanoTime() - started);
    }

    /** Live-only is not offered: like the .NET hub, subscribing replays everything the journal retains, then follows. */
    public FoundgineGatewaySubscription subscribe() { return subscribeAfter(0); }

    /** Replays retained events after {@code lastSequence}, then follows the live stream, gap-free. */
    public FoundgineGatewaySubscription subscribeAfter(long lastSequence) {
        if (lastSequence < 0) throw new IllegalArgumentException("lastSequence must not be negative");
        var id = UUID.randomUUID();
        var subscription = new FoundgineGatewaySubscription(eventStore, capacity, replayPageSize, lastSequence,
                () -> subscribers.remove(id));
        // Register before reading history so nothing published during replay is lost.
        subscribers.put(id, subscription);
        return subscription;
    }

    /** Number of currently registered live subscribers (diagnostics/tests). */
    public int subscriberCount() { return subscribers.size(); }
}
