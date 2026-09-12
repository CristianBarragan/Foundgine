package com.foundgine.providers.storage.sql.query;

import com.foundgine.core.semantic.planning.mutation.MutationValueReference;

/**
 * Binds one physical SQL parameter to a literal, mutation value, or execution
 * context path.
 */
public record SqlParameterBinding(String name, Object value, MutationValueReference source, String contextPath,
		Class<?> clrType) {
	public SqlParameterBinding(String name, Object value) {
		this(name, value, null, null, null);
	}

	public SqlParameterBinding(String name, Object value, MutationValueReference source) {
		this(name, value, source, null, null);
	}

	public SqlParameterBinding(String name, Object value, MutationValueReference source, String contextPath) {
		this(name, value, source, contextPath, null);
	}
}
