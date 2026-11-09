package com.foundgine.extensions.graphql;

import java.math.BigDecimal;
import java.time.Instant;
import java.time.LocalDateTime;
import java.time.OffsetDateTime;
import java.time.ZonedDateTime;

/** Maps Java field types to GraphQL scalar names, mirroring the C# CLR mapping. */
final class GraphQLTypeMapper {
    private GraphQLTypeMapper() { }

    static String map(Class<?> type) {
        if (type == String.class || type == char.class || type == Character.class || type == java.util.UUID.class)
            return "String";
        if (type == boolean.class || type == Boolean.class) return "Boolean";
        if (type == byte.class || type == Byte.class || type == short.class || type == Short.class
                || type == int.class || type == Integer.class || type == long.class || type == Long.class)
            return "Int";
        if (type == float.class || type == Float.class || type == double.class || type == Double.class
                || type == BigDecimal.class)
            return "Float";
        // C# DateTime / DateTimeOffset. Date-only values (C# DateOnly) intentionally stay String, as in C#.
        if (type == Instant.class || type == LocalDateTime.class || type == OffsetDateTime.class
                || type == ZonedDateTime.class || java.util.Date.class.isAssignableFrom(type))
            return "DateTime";
        if (type.isEnum()) return type.getSimpleName();
        return "String";
    }
}
