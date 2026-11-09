package com.foundgine.providers.eventstore;

import java.time.Instant;
import java.util.List;
import java.util.UUID;

/**
 * Foundgine-owned, tenant-scoped, per-stream ordered event log. Payloads are serialized with a caller-supplied
 * {@link PayloadCodec} (no reflection).
 */
public interface IEventStore {
    /** Appends idempotently by (tenant, eventId) and returns the stream sequence. */
    <T> long append(String tenantId, String streamId, UUID eventId, String eventType, Instant occurredAt,
            T payload, PayloadCodec<T> codec, String correlationId);

    <T> List<EventEnvelope<T>> read(String tenantId, String streamId, long afterSequence, Integer take, PayloadCodec<T> codec);

    /** The latest {@code take} events of one stream carrying the correlation id, oldest first. */
    <T> List<EventEnvelope<T>> readByCorrelation(String tenantId, String streamId, String correlationId, int take,
            PayloadCodec<T> codec);

    /** High-water mark of the stream; 0 when empty. Retention never lowers it. */
    long getLatestSequence(String tenantId, String streamId);

    int deleteExpired(Instant olderThan, int batchSize);

    default int deleteExpired(Instant olderThan) { return deleteExpired(olderThan, 1_000); }
}
