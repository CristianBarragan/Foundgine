package com.foundgine.core.execution;

/**
 * Port of {@code Foundgine.Core.Execution.ExecutionContextKeys}: reserved
 * runtime context keys used by provider execution for dynamic request values.
 */
public final class ExecutionContextKeys {

	public static final String PAGINATION_LIMIT = "foundgine.pagination.limit";
	public static final String PAGINATION_OFFSET = "foundgine.pagination.offset";
	public static final String PAGINATION_HAS_CURSOR = "foundgine.pagination.hasCursor";

	private ExecutionContextKeys() {
	}
}
