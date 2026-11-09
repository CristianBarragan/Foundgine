package com.foundgine.providers.gateway;
import java.time.Instant;
import java.util.Map;
import java.util.UUID;
/** Event envelope deliberately excludes request payloads, secrets and credentials. */
public record FoundgineGatewayEvent(UUID eventId, long sequence, Instant occurredAt, FoundgineGatewayEventType type,
        String subject, String correlationId, String providerId, Map<String, String> attributes) {
    public FoundgineGatewayEvent {
        eventId = eventId == null ? UUID.randomUUID() : eventId;
        occurredAt = occurredAt == null ? Instant.now() : occurredAt;
        if (type == null) throw new IllegalArgumentException("type is required");
        if (subject == null || subject.isBlank()) throw new IllegalArgumentException("subject is required");
        attributes = attributes == null ? Map.of() : Map.copyOf(attributes);
    }
    public static FoundgineGatewayEvent create(FoundgineGatewayEventType type, String subject,
            String correlationId, String providerId, Map<String, String> attributes) {
        return new FoundgineGatewayEvent(UUID.randomUUID(), 0L, Instant.now(), type, subject, correlationId, providerId, attributes);
    }
    /** Returns a copy carrying the sequence assigned by the event store. */
    public FoundgineGatewayEvent withSequence(long newSequence) {
        return new FoundgineGatewayEvent(eventId, newSequence, occurredAt, type, subject, correlationId, providerId, attributes);
    }
    /** Returns a copy with replaced attributes (used to strip reserved scoping attributes). */
    public FoundgineGatewayEvent withAttributes(Map<String, String> newAttributes) {
        return new FoundgineGatewayEvent(eventId, sequence, occurredAt, type, subject, correlationId, providerId, newAttributes);
    }
}
