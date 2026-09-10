package com.foundgine.core.execution;

import java.util.Map;

/**
 * Port of {@code Foundgine.Core.Execution.ExecutionRow}.
 *
 * <p>One row returned by a provider. {@code values} retain their
 * provider-facing names for diagnostics/backwards compatibility, while
 * {@code cells} provide stable provider-neutral identities for result
 * materialization.
 */
public record ExecutionRow(Map<String, Object> values, Map<ExecutionCellKey, Object> cells) {

    public ExecutionRow(Map<String, Object> values) {
        this(values, null);
    }

    public Map<ExecutionCellKey, Object> effectiveCells() {
        return cells != null ? cells : Map.of();
    }
}
