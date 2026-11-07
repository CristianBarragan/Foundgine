package com.foundgine.core.semantic.metadata;

import java.util.Objects;
import java.util.function.Function;

/**
 * A compile-time value conversion that may be used while resolving a connection.
 *
 * <p>Annotation-declared conversions name a static method and leave {@code convert} null.
 * Conversions declared through the fluent API carry the function itself, because a lambda has no
 * stable method name.
 */
public record ConversionMetadata(
        Class<?> sourceType, Class<?> targetType, String method, Function<Object, Object> convert) {

    public ConversionMetadata(Class<?> sourceType, Class<?> targetType, String method) {
        this(sourceType, targetType, method, null);
    }

    /** Creates fluent conversion metadata from a strongly typed function. */
    public static <S, T> ConversionMetadata fromFunction(
            Class<S> sourceType, Class<T> targetType, Function<S, T> convert) {
        Objects.requireNonNull(convert, "convert");
        @SuppressWarnings("unchecked")
        Function<Object, Object> erased = value -> convert.apply((S) value);
        return new ConversionMetadata(
                sourceType,
                targetType,
                "fluent:" + sourceType.getSimpleName() + "->" + targetType.getSimpleName(),
                erased);
    }
}
