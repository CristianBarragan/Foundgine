package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.FieldId;

import java.math.BigDecimal;
import java.math.BigInteger;
import java.time.Instant;
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.LocalTime;
import java.time.OffsetDateTime;
import java.util.Objects;

/**
 * One semantic field bound to a persistent attribute of an entity class.
 *
 * @param name entity attribute name (JPA attribute / Java property name)
 * @param type boxed Java type of the attribute
 */
public record OrmFieldBinding(
        FieldId id, String name, Class<?> type, boolean primaryKey, ColumnId column) {

    public OrmFieldBinding {
        Objects.requireNonNull(id, "id");
        Objects.requireNonNull(name, "name");
        type = box(Objects.requireNonNull(type, "type"));
    }

    /**
     * True only for the explicitly supported scalar types whose chronological/numeric
     * ordering has a portable semantic meaning. Arbitrary {@link Number} subclasses,
     * UUIDs, booleans, enums and arrays are refused rather than guessed.
     */
    public boolean isOrderable() {
        return type == Byte.class || type == Short.class || type == Integer.class
                || type == Long.class || type == Float.class || type == Double.class
                || type == BigDecimal.class || type == BigInteger.class
                || type == String.class
                || type == LocalDate.class
                || type == LocalDateTime.class
                || type == LocalTime.class
                || type == Instant.class
                || type == OffsetDateTime.class
                || type == BigDecimal.class
                || type == BigInteger.class;
    }

    static Class<?> box(Class<?> t) {
        if (!t.isPrimitive()) return t;
        if (t == int.class) return Integer.class;
        if (t == long.class) return Long.class;
        if (t == short.class) return Short.class;
        if (t == byte.class) return Byte.class;
        if (t == double.class) return Double.class;
        if (t == float.class) return Float.class;
        if (t == boolean.class) return Boolean.class;
        if (t == char.class) return Character.class;
        return t;
    }
}
