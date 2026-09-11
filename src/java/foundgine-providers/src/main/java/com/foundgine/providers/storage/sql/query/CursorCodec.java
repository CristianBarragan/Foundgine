package com.foundgine.providers.storage.sql.query;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.math.BigDecimal;
import java.nio.charset.StandardCharsets;
import java.time.*;
import java.util.*;
import java.util.Base64;
import java.util.UUID;

/** Versioned opaque compound cursor codec used by SQL keyset pagination. */
public final class CursorCodec {
    private static final int VERSION = 1;
    private static final ObjectMapper MAPPER = new ObjectMapper();
    private CursorCodec() {}

    public static String encode(List<?> values) {
        Objects.requireNonNull(values, "values");
        if (values.isEmpty()) throw new IllegalArgumentException("A compound cursor must contain at least one value.");
        try {
            Map<String,Object> payload = new LinkedHashMap<>();
            payload.put("Version", VERSION);
            payload.put("Values", values);
            return Base64.getEncoder().encodeToString(MAPPER.writeValueAsBytes(payload));
        } catch (Exception e) {
            throw new IllegalStateException("The pagination cursor could not be encoded.", e);
        }
    }

    public static List<JsonNode> decode(String cursor) {
        if (cursor == null || cursor.isBlank()) throw new IllegalArgumentException("The pagination cursor is invalid.");
        try {
            byte[] bytes = Base64.getDecoder().decode(cursor);
            JsonNode payload = MAPPER.readTree(new String(bytes, StandardCharsets.UTF_8));
            if (payload == null || payload.path("Version").asInt(-1) != VERSION || !payload.has("Values") || !payload.get("Values").isArray() || payload.get("Values").isEmpty())
                throw new IllegalArgumentException("The pagination cursor has an unsupported format.");
            List<JsonNode> result = new ArrayList<>();
            payload.get("Values").forEach(result::add);
            return List.copyOf(result);
        } catch (IllegalArgumentException e) { throw new IllegalArgumentException("The pagination cursor is invalid.", e); }
        catch (Exception e) { throw new IllegalArgumentException("The pagination cursor is invalid.", e); }
    }

    public static Object convertValue(JsonNode value, Class<?> targetType) {
        Objects.requireNonNull(value, "value"); Objects.requireNonNull(targetType, "targetType");
        if (value.isNull()) {
            if (!targetType.isPrimitive()) return null;
            throw new IllegalArgumentException("A null cursor value cannot be converted to non-nullable '" + targetType.getName() + "'.");
        }
        Class<?> type = boxed(targetType);
        if (type == String.class) return value.asText();
        if (type == UUID.class) return UUID.fromString(value.asText());
        if (type == Boolean.class) return value.asBoolean();
        if (type == Byte.class) return (byte)value.asInt();
        if (type == Short.class) return (short)value.asInt();
        if (type == Integer.class) return value.asInt();
        if (type == Long.class) return value.asLong();
        if (type == Float.class) return (float)value.asDouble();
        if (type == Double.class) return value.asDouble();
        if (type == BigDecimal.class) return value.decimalValue();
        if (type == Instant.class) return Instant.parse(value.asText());
        if (type == OffsetDateTime.class) return OffsetDateTime.parse(value.asText());
        if (type == ZonedDateTime.class) return ZonedDateTime.parse(value.asText());
        if (type == LocalDateTime.class) return LocalDateTime.parse(value.asText());
        if (type == LocalDate.class) return LocalDate.parse(value.asText());
        if (type == LocalTime.class) return LocalTime.parse(value.asText());
        try { return MAPPER.treeToValue(value, targetType); }
        catch (Exception e) { throw new IllegalArgumentException("Cursor value could not be converted to '" + targetType.getName() + "'.", e); }
    }
    private static Class<?> boxed(Class<?> type) {
        if (!type.isPrimitive()) return type;
        if (type == boolean.class) return Boolean.class; if (type == byte.class) return Byte.class; if (type == short.class) return Short.class;
        if (type == int.class) return Integer.class; if (type == long.class) return Long.class; if (type == float.class) return Float.class;
        if (type == double.class) return Double.class; if (type == char.class) return Character.class; return type;
    }
}
