package com.foundgine.providers.outbox;

import java.time.Instant;
import java.util.Map;
import java.util.UUID;

/**
 * A message to be delivered to infrastructure (broker, webhook, workflow signal) with at-least-once semantics.
 * {@code payloadJson} is already serialized by the caller. Consumers must de-duplicate by {@code id}.
 */
public record FoundgineOutboxMessage(UUID id, String tenantId, String topic, String payloadJson, String key,
        String correlationId, Map<String, String> headers, Instant createdAt) {
    public FoundgineOutboxMessage {
        headers = headers == null ? null : Map.copyOf(headers);
    }
    public FoundgineOutboxMessage(UUID id, String tenantId, String topic, String payloadJson) {
        this(id, tenantId, topic, payloadJson, null, null, null, null);
    }
}
