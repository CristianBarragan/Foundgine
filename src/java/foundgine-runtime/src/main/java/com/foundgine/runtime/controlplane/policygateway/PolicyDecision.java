package com.foundgine.runtime.controlplane.policygateway;

import java.util.List;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.PolicyGateway.PolicyDecision},
 * together with the {@code PolicyOutcome} enum declared in the same C# file.
 *
 * <p>The result of evaluating policy for a tool call. Always carries the
 * policy that produced it and a human-readable reason — a bare
 * {@link PolicyOutcome} is never surfaced on its own, matching the
 * explainability requirement already established by
 * {@code com.foundgine.runtime.controlplane.riskscoring.RiskScore.RiskSignal}.
 */
public record PolicyDecision(
        PolicyOutcome outcome,
        String policyId,
        String reason,
        List<String> obligationTags) {

    /** The three outcomes a policy evaluation can produce for a tool call. */
    public enum PolicyOutcome {
        ALLOW, REQUIRE_APPROVAL, DENY
    }

    public PolicyDecision {
        obligationTags = List.copyOf(obligationTags);
    }

    public static PolicyDecision allow(String policyId, String reason) {
        return new PolicyDecision(PolicyOutcome.ALLOW, policyId, reason, List.of());
    }

    public static PolicyDecision deny(String policyId, String reason) {
        return new PolicyDecision(PolicyOutcome.DENY, policyId, reason, List.of());
    }

    public static PolicyDecision requireApproval(String policyId, String reason) {
        return requireApproval(policyId, reason, null);
    }

    public static PolicyDecision requireApproval(String policyId, String reason, List<String> obligationTags) {
        return new PolicyDecision(PolicyOutcome.REQUIRE_APPROVAL, policyId, reason,
                obligationTags == null ? List.of() : obligationTags);
    }
}
