package com.foundgine.core.execution.mutation;

import com.foundgine.core.abstractions.FieldId;

import java.util.Map;

/** Port of {@code Foundgine.Core.Execution.Mutation.MutationResult}. */
public record MutationResult(int affectedRows, Map<FieldId, Object> returnedValues) {

	public MutationResult(int affectedRows) {
		this(affectedRows, null);
	}

	/**
	 * Compatibility name used by the PostgreSQL E2E integration tests and older
	 * callers. {@link #returnedValues()} remains the canonical result
	 * representation.
	 */
	public Map<FieldId, Object> returnedFields() {
		return returnedValues;
	}
}
