package com.foundgine.providers.storage.orm;

import java.math.BigDecimal;
import java.math.BigInteger;
import java.time.Instant;
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.LocalTime;
import java.time.OffsetDateTime;
import java.util.UUID;

/** Value conversion/normalization shared by lowering, cursors and the in-memory source. */
public final class OrmValues {
    private OrmValues() {}

    /** Converts {@code value} to {@code target} (boxed), failing closed on lossy conversions. */
    public static Object coerce(Object value, Class<?> target) {
        if (value == null) return null;
        Class<?> t = OrmFieldBinding.box(target);
        if (t.isInstance(value) && !(value instanceof String && t != String.class)) return value;
        try {
            if (value instanceof Number n && Number.class.isAssignableFrom(t)) return number(n, t);
            if (value instanceof Number n && t == String.class) return n.toString();
            if (value instanceof String s) return parse(s, t);
            if (value instanceof Boolean b && t == String.class) return b.toString();
        } catch (RuntimeException e) {
            throw new IllegalArgumentException(
                    "Value '" + value + "' cannot be converted to " + t.getSimpleName() + ".", e);
        }
        throw new IllegalArgumentException(
                "Value of type "
                        + value.getClass().getSimpleName()
                        + " cannot be converted to "
                        + t.getSimpleName()
                        + ".");
    }

    private static Object number(Number n, Class<?> t) {
        BigDecimal d = n instanceof BigDecimal bd ? bd : new BigDecimal(n.toString());
        if (t == Integer.class) return d.intValueExact();
        if (t == Long.class) return d.longValueExact();
        if (t == Short.class) return d.shortValueExact();
        if (t == Byte.class) return d.byteValueExact();
        if (t == BigInteger.class) return d.toBigIntegerExact();
        if (t == BigDecimal.class) return d;
        if (t == Double.class) return d.doubleValue();
        if (t == Float.class) return d.floatValue();
        throw new IllegalArgumentException("Unsupported numeric type " + t.getSimpleName());
    }

    @SuppressWarnings({"unchecked", "rawtypes"})
    private static Object parse(String s, Class<?> t) {
        if (t == String.class) return s;
        if (t == Integer.class) return Integer.valueOf(s);
        if (t == Long.class) return Long.valueOf(s);
        if (t == Short.class) return Short.valueOf(s);
        if (t == Byte.class) return Byte.valueOf(s);
        if (t == BigInteger.class) return new BigInteger(s);
        if (t == BigDecimal.class) return new BigDecimal(s);
        if (t == Double.class) return Double.valueOf(s);
        if (t == Float.class) return Float.valueOf(s);
        if (t == Boolean.class) {
            if (s.equalsIgnoreCase("true") || s.equalsIgnoreCase("false")) {
                return Boolean.valueOf(s);
            }
            throw new IllegalArgumentException("not a boolean");
        }
        if (t == UUID.class) return UUID.fromString(s);
        if (t == LocalDate.class) return LocalDate.parse(s);
        if (t == LocalDateTime.class) return LocalDateTime.parse(s);
        if (t == LocalTime.class) return LocalTime.parse(s);
        if (t == Instant.class) return Instant.parse(s);
        if (t == OffsetDateTime.class) return OffsetDateTime.parse(s);
        if (t.isEnum()) return Enum.valueOf((Class<? extends Enum>) t, s);
        throw new IllegalArgumentException("Unsupported target type " + t.getSimpleName());
    }

    /** Join-key identity: int 5 and long 5 and 5.0 match; null never matches. */
    public static String normalize(Object value) {
        if (value == null) return null;
        if (value instanceof Number n) {
            try {
                BigDecimal d = n instanceof BigDecimal bd ? bd : new BigDecimal(n.toString());
                d = d.signum() == 0 ? BigDecimal.ZERO : d.stripTrailingZeros();
                return d.toPlainString();
            } catch (NumberFormatException e) {
                return n.toString();
            }
        }
        return value.toString();
    }

    /** Equality that treats numerically equal numbers of different types as equal. */
    public static boolean valueEquals(Object a, Object b) {
        if (a == null || b == null) return a == b;
        if (a instanceof Number && b instanceof Number) return compare(a, b) == 0;
        return a.equals(b);
    }

    /** Total order for orderable values; nulls sort first. */
    @SuppressWarnings({"unchecked", "rawtypes"})
    public static int compare(Object a, Object b) {
        if (a == b) return 0;
        if (a == null) return -1;
        if (b == null) return 1;
        if (a instanceof Number x && b instanceof Number y) {
            return new BigDecimal(x.toString()).compareTo(new BigDecimal(y.toString()));
        }
        if (a instanceof Comparable c && a.getClass().isInstance(b)) return c.compareTo(b);
        throw new IllegalArgumentException(
                "Values of type "
                        + a.getClass().getSimpleName()
                        + " and "
                        + b.getClass().getSimpleName()
                        + " are not comparable.");
    }
}
