package com.foundgine.core.semantic.metadata;

public record ConnectionFieldMetadata(String sourceMember, String targetMember, Class<?> sourceType,
		Class<?> targetType, String converter) {
	public ConnectionFieldMetadata(String sourceMember, String targetMember, Class<?> sourceType, Class<?> targetType) {
		this(sourceMember, targetMember, sourceType, targetType, null);
	}
}
