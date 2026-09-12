package com.foundgine.core.execution;

import java.util.List;

/**
 * Port of {@code Foundgine.Core.Execution.ExecutionResult} (a C#
 * {@code sealed record} with {@code PageInfo}, {@code Evidence}, and
 * {@code Receipt} all defaulting to {@code null}). Java has no optional/default
 * positional parameters, so only the all-{@code null}-trailing-fields
 * convenience ({@link #ExecutionResult(List)}) is provided as an overload;
 * construct the canonical record directly for any other combination.
 */
public record ExecutionResult(List<ExecutionRow> rows, ExecutionPageInfo pageInfo, ExecutionEvidence evidence,
		ExecutionReceipt receipt) {

	public ExecutionResult(List<ExecutionRow> rows) {
		this(rows, null, null, null);
	}
}
