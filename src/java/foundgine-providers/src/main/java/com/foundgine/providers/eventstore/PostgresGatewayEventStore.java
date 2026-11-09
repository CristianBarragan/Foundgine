package com.foundgine.providers.eventstore;

import com.foundgine.providers.gateway.FoundgineGatewayEvent;
import com.foundgine.providers.gateway.FoundgineGatewayEventJson;
import com.foundgine.providers.gateway.FoundgineProviderCapability;
import com.foundgine.providers.gateway.FoundgineProviderKind;
import com.foundgine.providers.gateway.IFoundgineProvider;
import com.foundgine.providers.gateway.events.IFoundgineGatewayEventStore;
import java.util.ArrayList;
import java.util.List;

/**
 * Adapts the tenant/stream {@link IEventStore} to the gateway's global, ordered journal contract. The whole journal
 * is one stream, so its sequence is gap-free and monotonic across instances; appends are serialized by the store
 * (correctness over throughput). Idempotent by the event's stable event id.
 */
public final class PostgresGatewayEventStore implements IFoundgineGatewayEventStore, IFoundgineProvider {
    static final PayloadCodec<FoundgineGatewayEvent> CODEC = new PayloadCodec<>() {
        @Override public String toJson(FoundgineGatewayEvent payload) { return FoundgineGatewayEventJson.toJson(payload); }
        @Override public FoundgineGatewayEvent fromJson(String json) { return FoundgineGatewayEventJson.fromJson(json); }
    };

    private final IEventStore store;
    private final PostgresGatewayEventStoreOptions options;

    public PostgresGatewayEventStore(IEventStore store, PostgresGatewayEventStoreOptions options) {
        if (store == null) throw new IllegalArgumentException("store is required");
        if (options == null) throw new IllegalArgumentException("options is required");
        options.validate();
        this.store = store;
        this.options = options;
    }

    @Override public String id() { return "foundgine.postgres.gateway-journal"; }
    @Override public String name() { return "PostgreSQL gateway event journal"; }
    @Override public String version() { return "1.0.0"; }
    @Override public FoundgineProviderKind kind() { return FoundgineProviderKind.EVENT_STORE; }
    /** No tenant isolation: the gateway journal is a single system-tenant log. */
    @Override public long capabilities() {
        return FoundgineProviderCapability.mask(FoundgineProviderCapability.DURABLE_STORAGE,
                FoundgineProviderCapability.ORDERED_EVENTS, FoundgineProviderCapability.EVENT_REPLAY,
                FoundgineProviderCapability.TRANSACTIONAL_APPEND);
    }

    @Override public FoundgineGatewayEvent append(FoundgineGatewayEvent e) {
        if (e == null) throw new IllegalArgumentException("gatewayEvent is required");
        long sequence = store.append(options.tenantId(), options.streamId(), e.eventId(),
                FoundgineGatewayEventJson.typeName(e.type()), e.occurredAt(), e.withSequence(0), CODEC, e.correlationId());
        return e.withSequence(sequence);
    }

    @Override public long getLatestSequence() { return store.getLatestSequence(options.tenantId(), options.streamId()); }

    @Override public List<FoundgineGatewayEvent> readAfter(long sequence, int limit) {
        if (sequence < 0) throw new IllegalArgumentException("sequence must not be negative");
        if (limit < 1 || limit > 10_000) throw new IllegalArgumentException("limit must be 1-10,000");
        var result = new ArrayList<FoundgineGatewayEvent>(Math.min(limit, 1024));
        long cursor = sequence;
        while (result.size() < limit) {
            int take = Math.min(options.pageSize(), limit - result.size());
            var page = store.read(options.tenantId(), options.streamId(), cursor, take, CODEC);
            for (var envelope : page) result.add(envelope.payload().withSequence(envelope.sequence()));
            if (page.size() < take) break;
            cursor = page.get(page.size() - 1).sequence();
        }
        return result;
    }

    /** Returns at most {@code pageSize} latest matches. */
    @Override public List<FoundgineGatewayEvent> readByCorrelationId(String correlationId, int limit) {
        if (correlationId == null || correlationId.isBlank()) throw new IllegalArgumentException("correlationId is required");
        if (limit < 1 || limit > 10_000) throw new IllegalArgumentException("limit must be 1-10,000");
        var page = store.readByCorrelation(options.tenantId(), options.streamId(), correlationId,
                Math.min(limit, options.pageSize()), CODEC);
        return page.stream().map(e -> e.payload().withSequence(e.sequence())).toList();
    }
}
