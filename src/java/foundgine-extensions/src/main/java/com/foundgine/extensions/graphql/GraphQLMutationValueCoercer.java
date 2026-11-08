package com.foundgine.extensions.graphql;

import com.foundgine.core.semantic.metadata.FieldMetadata;
import java.math.BigDecimal;
import java.math.BigInteger;
import java.math.RoundingMode;
import java.time.DateTimeException;
import java.time.Instant;
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.LocalTime;
import java.time.OffsetDateTime;
import java.util.Map;
import java.util.UUID;
import java.util.regex.Pattern;

/**
 * Port of the C# {@code HotChocolateMutationAdapter.CoerceMutationValue}: converts a resolved GraphQL runtime value to
 * the CLR (here: Java) type declared by the field metadata.
 *
 * <h2>CLR to Java type mapping</h2>
 *
 * <pre>
 * Guid -> UUID            DateTime -> LocalDateTime      DateTimeOffset -> OffsetDateTime (and Instant)
 * DateOnly -> LocalDate   TimeOnly -> LocalTime          decimal -> BigDecimal       sbyte -> Byte
 * </pre>
 *
 * Java has no unsigned integers, so the C# {@code byte}/{@code ushort}/{@code uint}/{@code ulong} branches have no
 * counterpart. Error messages keep the C# type names ({@code String}, {@code Int32}, ...) so clients and tests see the
 * same text on both platforms.
 *
 * <p>Known, intentional differences: a value of an unrelated type given to a numeric/boolean field raises the usual
 * type error (C# {@code Convert.ChangeType} would throw an uncaught {@code InvalidCastException}); GUID parsing accepts
 * the dashed form, the 32-digit form and brace/parenthesis wrapped forms; enum parsing is by name, ignoring case (C#
 * additionally accepts numeric text).
 */
final class GraphQLMutationValueCoercer {
    private static final Pattern DASHED_GUID =
            Pattern.compile("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
    private static final Pattern PLAIN_GUID = Pattern.compile("[0-9a-fA-F]{32}");

    private GraphQLMutationValueCoercer() {}

    static Object coerce(FieldMetadata field, Object value, String path) {
        Class<?> declaredType = field.clrType();
        Class<?> type = box(declaredType);

        if (value == null) {
            if (!declaredType.isPrimitive()) return null;
            throw new IllegalStateException("GraphQL value at '" + path + "' cannot be null.");
        }

        if (type.isInstance(value)) return value;

        try {
            if (type == String.class) throw typeError(path, type, value);

            if (type == UUID.class) {
                if (value instanceof String text) {
                    var guid = tryParseGuid(text);
                    if (guid != null) return guid;
                }
                throw typeError(path, type, value);
            }

            if (type.isEnum()) {
                if (value instanceof String name)
                    for (var constant : type.getEnumConstants())
                        if (((Enum<?>) constant).name().equalsIgnoreCase(name.trim())) return constant;
                throw typeError(path, type, value);
            }

            if (type == LocalDateTime.class && value instanceof String text) return parseLocalDateTime(text.trim());
            if (type == OffsetDateTime.class && value instanceof String text) return OffsetDateTime.parse(text.trim());
            if (type == Instant.class && value instanceof String text) return parseInstant(text.trim());
            if (type == LocalDate.class && value instanceof String text) return LocalDate.parse(text.trim());
            if (type == LocalTime.class && value instanceof String text) return LocalTime.parse(text.trim());

            if (type == Boolean.class) return toBoolean(value, path, type);

            if (isNumeric(type)) return toNumber(value, type, path);
        } catch (ArithmeticException | IllegalArgumentException | DateTimeException exception) {
            throw new IllegalStateException(
                    "GraphQL value for '" + path + "' could not be converted to '" + clrName(type) + "'.", exception);
        }

        throw typeError(path, type, value);
    }

    // ---------------------------------------------------------------------------------------------------------------

    private static boolean isNumeric(Class<?> type) {
        return type == Byte.class || type == Short.class || type == Integer.class || type == Long.class
                || type == Float.class || type == Double.class || type == BigDecimal.class;
    }

    /** {@code Convert.ToBoolean}: bool, "true"/"false" text, or a number compared with zero. */
    private static Boolean toBoolean(Object value, String path, Class<?> type) {
        if (value instanceof Boolean b) return b;
        if (value instanceof String text) {
            var trimmed = text.trim();
            if (trimmed.equalsIgnoreCase("true")) return Boolean.TRUE;
            if (trimmed.equalsIgnoreCase("false")) return Boolean.FALSE;
            throw new NumberFormatException("Not a valid Boolean: " + text);
        }
        if (value instanceof Number number) return toDecimal(number).signum() != 0;
        throw typeError(path, type, value);
    }

    private static Object toNumber(Object value, Class<?> type, String path) {
        if (type == Float.class || type == Double.class) {
            double d;
            if (value instanceof String text) d = Double.parseDouble(text.trim());
            else if (value instanceof Number number) d = number.doubleValue();
            else if (value instanceof Boolean b) d = b ? 1 : 0;
            else throw typeError(path, type, value);
            return type == Float.class ? (Object) (float) d : (Object) d;
        }

        // Integral and decimal targets share an exact decimal intermediate.
        BigDecimal decimal;
        if (value instanceof String text) {
            // Convert.ToInt32("1.5") is a FormatException while Convert.ToDecimal("1.5") is fine.
            decimal = type == BigDecimal.class ? new BigDecimal(text.trim()) : new BigDecimal(new BigInteger(text.trim()));
        } else if (value instanceof Number number) {
            decimal = toDecimal(number);
        } else if (value instanceof Boolean b) {
            decimal = b ? BigDecimal.ONE : BigDecimal.ZERO;
        } else {
            throw typeError(path, type, value);
        }

        if (type == BigDecimal.class) return decimal;

        // Convert rounds to nearest, ties to even, then range-checks (OverflowException).
        var whole = decimal.setScale(0, RoundingMode.HALF_EVEN);
        if (type == Long.class) return whole.longValueExact();
        if (type == Integer.class) return whole.intValueExact();
        if (type == Short.class) return whole.shortValueExact();
        return whole.byteValueExact();
    }

    private static BigDecimal toDecimal(Number number) {
        if (number instanceof BigDecimal d) return d;
        if (number instanceof BigInteger i) return new BigDecimal(i);
        if (number instanceof Double || number instanceof Float) {
            double d = number.doubleValue();
            if (Double.isNaN(d) || Double.isInfinite(d)) throw new ArithmeticException("Value is not finite.");
            return BigDecimal.valueOf(d);
        }
        return BigDecimal.valueOf(number.longValue());
    }

    private static LocalDateTime parseLocalDateTime(String text) {
        try {
            return LocalDateTime.parse(text);
        } catch (DateTimeException first) {
            try {
                return OffsetDateTime.parse(text).toLocalDateTime();
            } catch (DateTimeException second) {
                return LocalDate.parse(text).atStartOfDay();
            }
        }
    }

    private static Instant parseInstant(String text) {
        try {
            return Instant.parse(text);
        } catch (DateTimeException first) {
            return OffsetDateTime.parse(text).toInstant();
        }
    }

    private static UUID tryParseGuid(String text) {
        var s = text.trim();
        if (s.length() > 2
                && ((s.startsWith("{") && s.endsWith("}")) || (s.startsWith("(") && s.endsWith(")"))))
            s = s.substring(1, s.length() - 1);

        if (PLAIN_GUID.matcher(s).matches())
            s = s.substring(0, 8) + "-" + s.substring(8, 12) + "-" + s.substring(12, 16) + "-" + s.substring(16, 20)
                    + "-" + s.substring(20);

        return DASHED_GUID.matcher(s).matches() ? UUID.fromString(s) : null;
    }

    // ---------------------------------------------------------------------------------------------------------------

    private static Class<?> box(Class<?> type) {
        if (!type.isPrimitive()) return type;
        if (type == int.class) return Integer.class;
        if (type == long.class) return Long.class;
        if (type == boolean.class) return Boolean.class;
        if (type == double.class) return Double.class;
        if (type == float.class) return Float.class;
        if (type == short.class) return Short.class;
        if (type == byte.class) return Byte.class;
        if (type == char.class) return Character.class;
        return type;
    }

    private static IllegalStateException typeError(String path, Class<?> expected, Object value) {
        return new IllegalStateException("GraphQL value for '" + path + "' expects '" + clrName(expected)
                + "', but received '" + clrName(value.getClass()) + "'.");
    }

    /** The .NET type name a C# user would see for the equivalent Java type. */
    static String clrName(Class<?> type) {
        if (type == String.class) return "String";
        if (type == Integer.class) return "Int32";
        if (type == Long.class) return "Int64";
        if (type == Short.class) return "Int16";
        if (type == Byte.class) return "SByte";
        if (type == Boolean.class) return "Boolean";
        if (type == Double.class) return "Double";
        if (type == Float.class) return "Single";
        if (type == BigDecimal.class) return "Decimal";
        if (type == UUID.class) return "Guid";
        if (type == LocalDateTime.class) return "DateTime";
        if (type == OffsetDateTime.class || type == Instant.class) return "DateTimeOffset";
        if (type == LocalDate.class) return "DateOnly";
        if (type == LocalTime.class) return "TimeOnly";
        if (Map.class.isAssignableFrom(type)) return "Dictionary";
        return type.getSimpleName();
    }
}
