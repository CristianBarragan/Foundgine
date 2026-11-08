package com.foundgine.providers.gateway.events;

import com.foundgine.providers.gateway.FoundgineGatewayEvent;
import com.foundgine.providers.gateway.FoundgineProviderCapability;
import com.foundgine.providers.gateway.FoundgineProviderKind;
import com.foundgine.providers.gateway.IFoundgineProvider;
import java.util.ArrayList;
import java.util.List;

/**
 * Bounded, process-local event journal for development and single-instance deployments.
 * Not durable across restarts and must not be presented as a production audit store.
 */
public final class InMemoryFoundgineGatewayEventStore implements IFoundgineGatewayEventStore, IFoundgineProvider {
    private final Object gate = new Object();
    private final ArrayList<FoundgineGatewayEvent> events = new ArrayList<>();
    private final int capacity;
    private long sequence;

    public InMemoryFoundgineGatewayEventStore() { this(10_000); }
    public InMemoryFoundgineGatewayEventStore(int capacity) {
        if (capacity < 100) throw new IllegalArgumentException("Event retention must be at least 100.");
        this.capacity = capacity;
    }

    @Override public String id() { return "foundgine.inmemory.eventstore"; }
    @Override public String name() { return "In-memory gateway event journal"; }
    @Override public String version() { return "1.0.0"; }
    @Override public FoundgineProviderKind kind() { return FoundgineProviderKind.EVENT_STORE; }
    /** Ordered and replayable within one process; deliberately not durable and not tenant-isolated. */
    @Override public long capabilities() {
        return FoundgineProviderCapability.mask(FoundgineProviderCapability.ORDERED_EVENTS,
                FoundgineProviderCapability.EVENT_REPLAY, FoundgineProviderCapability.TRANSACTIONAL_APPEND);
    }

    @Override public FoundgineGatewayEvent append(FoundgineGatewayEvent gatewayEvent) {
        if (gatewayEvent == null) throw new IllegalArgumentException("gatewayEvent is required");
        synchronized (gate) {
            var stored = gatewayEvent.withSequence(++sequence);
            events.add(stored);
            if (events.size() > capacity) events.subList(0, events.size() - capacity).clear();
            return stored;
        }
    }
    @Override public long getLatestSequence() { synchronized (gate) { return sequence; } }
    @Override public List<FoundgineGatewayEvent> readAfter(long after, int limit) {
        validateLimit(limit);
        synchronized (gate) {
            return events.stream().filter(e -> e.sequence() > after).limit(limit).toList();
        }
    }
    @Override public List<FoundgineGatewayEvent> readByCorrelationId(String correlationId, int limit) {
        if (correlationId == null || correlationId.isBlank()) throw new IllegalArgumentException("correlationId is required");
        validateLimit(limit);
        synchronized (gate) {
            var matches = events.stream().filter(e -> correlationId.equals(e.correlationId())).toList();
            return matches.subList(Math.max(0, matches.size() - limit), matches.size());
        }
    }
    private static void validateLimit(int limit) {
        if (limit < 1 || limit > 10_000) throw new IllegalArgumentException("limit must be 1-10,000");
    }
}
