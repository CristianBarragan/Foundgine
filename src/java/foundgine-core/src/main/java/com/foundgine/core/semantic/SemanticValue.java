package com.foundgine.core.semantic;

import java.math.BigDecimal;
import java.time.*;
import java.util.*;

/** Provider-independent canonical semantic value. */
public record SemanticValue(SemanticValueKind kind, Object value) {
    public static final SemanticValue NULL = new SemanticValue(SemanticValueKind.NULL, null);
    public static SemanticValue from(Object value) {
        if (value == null) return NULL;
        if (value instanceof String || value instanceof Character) return new SemanticValue(SemanticValueKind.STRING, value.toString());
        if (value instanceof Boolean) return new SemanticValue(SemanticValueKind.BOOLEAN, value);
        if (value instanceof Byte || value instanceof Short || value instanceof Integer || value instanceof Long)
            return new SemanticValue(SemanticValueKind.INT64, ((Number)value).longValue());
        if (value instanceof Float || value instanceof Double || value instanceof BigDecimal)
            return new SemanticValue(SemanticValueKind.DECIMAL, new BigDecimal(value.toString()));
        if (value instanceof Instant || value instanceof LocalDateTime || value instanceof OffsetDateTime || value instanceof ZonedDateTime || value instanceof LocalDate || value instanceof java.util.Date)
            return new SemanticValue(SemanticValueKind.DATETIME, value);
        if (value instanceof UUID) return new SemanticValue(SemanticValueKind.GUID, value);
        if (value instanceof Enum<?>) return new SemanticValue(SemanticValueKind.ENUM, value.toString());
        if (value instanceof Iterable<?> it) { List<SemanticValue> list = new ArrayList<>(); it.forEach(v -> list.add(from(v))); return new SemanticValue(SemanticValueKind.LIST, List.copyOf(list)); }
        return new SemanticValue(SemanticValueKind.OBJECT, value.toString());
    }
    @Override public String toString() { return kind == SemanticValueKind.NULL ? "null" : String.valueOf(value); }
}
