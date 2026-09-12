package com.foundgine.core.semantic.metadata;

public record ConversionMetadata(Class<?> sourceType, Class<?> targetType, String method) {
}
