package com.foundgine.core.semantic;

import java.math.BigDecimal;
import java.time.temporal.TemporalAccessor;
import java.util.Collection;
import java.util.Objects;
import java.util.UUID;

/** Provider-independent semantic type information. */
public sealed interface SemanticType permits SemanticType.Scalar, SemanticType.EnumType, SemanticType.ObjectType, SemanticType.CollectionType {
    record Scalar(SemanticScalarKind kind) implements SemanticType { public Scalar { Objects.requireNonNull(kind); } }
    record EnumType(String name) implements SemanticType { public EnumType { Objects.requireNonNull(name); } }
    record ObjectType(String name) implements SemanticType { public ObjectType { Objects.requireNonNull(name); } }
    record CollectionType(SemanticType elementType) implements SemanticType { public CollectionType { Objects.requireNonNull(elementType); } }

    static SemanticType fromClass(Class<?> type) {
        Objects.requireNonNull(type);
        if (type == byte[].class) return new Scalar(SemanticScalarKind.BYTES);
        if (type.isArray()) return new CollectionType(fromClass(type.getComponentType()));
        if (type.isEnum()) return new EnumType(type.getSimpleName());
        if (type == String.class || type == Character.class || type == char.class) return new Scalar(SemanticScalarKind.STRING);
        if (type == boolean.class || type == Boolean.class) return new Scalar(SemanticScalarKind.BOOLEAN);
        if (type == byte.class || type == Byte.class || type == short.class || type == Short.class || type == int.class || type == Integer.class)
            return new Scalar(SemanticScalarKind.INT32);
        if (type == long.class || type == Long.class) return new Scalar(SemanticScalarKind.INT64);
        if (type == float.class || type == Float.class || type == double.class || type == Double.class || type == BigDecimal.class)
            return new Scalar(SemanticScalarKind.DECIMAL);
        if (TemporalAccessor.class.isAssignableFrom(type) || java.util.Date.class.isAssignableFrom(type)) return new Scalar(SemanticScalarKind.DATETIME);
        if (type == UUID.class) return new Scalar(SemanticScalarKind.GUID);
        if (Collection.class.isAssignableFrom(type)) return new CollectionType(new ObjectType("Object"));
        return new ObjectType(type.getSimpleName());
    }
}
