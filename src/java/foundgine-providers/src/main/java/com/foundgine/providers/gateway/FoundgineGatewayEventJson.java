package com.foundgine.providers.gateway;

import com.foundgine.providers.json.FoundgineJson;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;

/**
 * Wire format of a {@link FoundgineGatewayEvent}, compatible with the .NET {@code FoundgineGatewayJsonContext}
 * (camelCase properties in declaration order, PascalCase enum names, ISO-8601 timestamps, unmapped members rejected)
 * so a journal written by one runtime can be replayed by the other.
 */
public final class FoundgineGatewayEventJson {
    private FoundgineGatewayEventJson() { }

    public static String toJson(FoundgineGatewayEvent e) {
        var sb = new StringBuilder(256);
        sb.append("{\"sequence\":").append(e.sequence());
        sb.append(",\"occurredAt\":"); FoundgineJson.writeString(sb, e.occurredAt().toString());
        sb.append(",\"type\":"); FoundgineJson.writeString(sb, typeName(e.type()));
        sb.append(",\"subject\":"); FoundgineJson.writeString(sb, e.subject());
        sb.append(",\"correlationId\":"); FoundgineJson.writeNullableString(sb, e.correlationId());
        sb.append(",\"providerId\":"); FoundgineJson.writeNullableString(sb, e.providerId());
        sb.append(",\"attributes\":"); FoundgineJson.writeStringMap(sb, e.attributes());
        sb.append(",\"eventId\":"); FoundgineJson.writeString(sb, e.eventId().toString());
        return sb.append('}').toString();
    }

    public static FoundgineGatewayEvent fromJson(String json) {
        if (!(FoundgineJson.parse(json) instanceof Map<?, ?> raw)) throw new IllegalArgumentException("Gateway event JSON object expected.");
        var m = new LinkedHashMap<String, Object>();
        for (var en : raw.entrySet()) m.put((String) en.getKey(), en.getValue());
        for (var key : m.keySet()) {
            switch (key) {
                case "sequence", "occurredAt", "type", "subject", "correlationId", "providerId", "attributes", "eventId" -> { }
                default -> throw new IllegalArgumentException("Unmapped member '" + key + "' in gateway event JSON.");
            }
        }
        long sequence = m.get("sequence") instanceof Long l ? l : 0L;
        Instant at = parseInstant(required(m, "occurredAt"));
        var type = parseType(required(m, "type"));
        String subject = required(m, "subject");
        String correlation = optional(m, "correlationId");
        String provider = optional(m, "providerId");
        Map<String, String> attributes = Map.of();
        if (m.get("attributes") != null) {
            if (!(m.get("attributes") instanceof Map<?, ?> a)) throw new IllegalArgumentException("attributes must be an object.");
            var copy = new LinkedHashMap<String, String>();
            for (var en : a.entrySet()) {
                if (!(en.getValue() instanceof String s)) throw new IllegalArgumentException("attributes values must be strings.");
                copy.put((String) en.getKey(), s);
            }
            attributes = copy;
        }
        UUID id = m.get("eventId") == null ? null : UUID.fromString(required(m, "eventId"));
        return new FoundgineGatewayEvent(id, sequence, at, type, subject, correlation, provider, attributes);
    }

    /** CAPABILITY_CHANGED -> CapabilityChanged. */
    public static String typeName(FoundgineGatewayEventType type) {
        var sb = new StringBuilder();
        for (String part : type.name().split("_")) {
            sb.append(part.charAt(0)).append(part.substring(1).toLowerCase(Locale.ROOT));
        }
        return sb.toString();
    }

    public static FoundgineGatewayEventType parseType(String name) {
        for (var t : FoundgineGatewayEventType.values()) if (typeName(t).equals(name)) return t;
        throw new IllegalArgumentException("Unknown gateway event type '" + name + "'.");
    }

    private static Instant parseInstant(String text) {
        try { return Instant.parse(text); }
        catch (java.time.format.DateTimeParseException ex) { return OffsetDateTime.parse(text).toInstant(); }
    }

    private static String required(Map<String, Object> m, String key) {
        if (!(m.get(key) instanceof String s)) throw new IllegalArgumentException("Property '" + key + "' is required.");
        return s;
    }

    private static String optional(Map<String, Object> m, String key) {
        Object v = m.get(key);
        if (v == null) return null;
        if (v instanceof String s) return s;
        throw new IllegalArgumentException("Property '" + key + "' must be a string or null.");
    }
}
