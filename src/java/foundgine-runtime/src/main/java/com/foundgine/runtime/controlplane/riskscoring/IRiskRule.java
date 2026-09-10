package com.foundgine.runtime.controlplane.riskscoring;

import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.RiskScoring.IRiskRule}.
 *
 * <p>A single, independently-testable risk factor (e.g. "tool is tagged
 * destructive", "caller has no prior successful calls this session").
 * Unlike {@code IRoutingRule} and {@code IPolicyRule}, risk rules don't
 * abstain — every rule contributes a signal (possibly zero-weight), so
 * scoring is a pure sum, not a resolution order.
 */
public interface IRiskRule {
    RiskScore.RiskSignal evaluate(String toolName, SecurityExecutionContext security);
}
