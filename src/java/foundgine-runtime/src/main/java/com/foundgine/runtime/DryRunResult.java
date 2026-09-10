package com.foundgine.runtime;

import com.foundgine.core.semantic.planning.PlanInspection;

/**
 * Port of {@code Foundgine.Runtime.DryRunResult}.
 *
 * <p>Result of planning an intent without executing it. The returned
 * fingerprint identifies the exact authorized plan that was inspected.
 *
 * <p>C#'s {@code ExecutionRequiredApproval = false} default parameter is
 * ported as a two-constructor pair, matching this port's established
 * convention for optional-parameter C# constructors.
 */
public record DryRunResult(PlanInspection inspection, boolean executionRequiredApproval) {

    public DryRunResult(PlanInspection inspection) {
        this(inspection, false);
    }
}
