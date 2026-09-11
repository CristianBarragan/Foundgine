package com.foundgine.core.execution;

/** Port of {@code Foundgine.Core.Execution.ExecutionPageInfo}. */
public record ExecutionPageInfo(
        String startCursor,
        String endCursor,
        boolean hasNextPage,
        boolean hasPreviousPage) {
}
