package com.foundgine.providers.eventstore;

import java.time.Instant;
import java.util.UUID;

/** A stored event: tenant-scoped, per-stream ordered. */
public record EventEnvelope<T>(UUID eventId, String tenantId, String streamId, long sequence,
        String eventType, Instant occurredAt, T payload, String correlationId) { }
