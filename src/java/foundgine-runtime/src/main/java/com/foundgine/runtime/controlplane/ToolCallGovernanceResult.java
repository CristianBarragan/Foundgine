package com.foundgine.runtime.controlplane;

import com.foundgine.runtime.controlplane.approvals.ApprovalRequest;
import com.foundgine.runtime.controlplane.policygateway.PolicyDecision;
import com.foundgine.runtime.routing.TaskContract;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.ToolCallGovernanceResult}.
 *
 * <p>
 * The result of governing a tool call: what happened, and — if permitted — how
 * it should run.
 *
 * <p>
 * <b>Porting decision:</b> {@code TaskContract?} and {@code ApprovalRequest?}
 * stay plain nullable fields (rather than {@code Optional}) because this is a
 * terminal result record consumed by callers, not an abstention signal threaded
 * through a rule chain — the same distinction already drawn for other result
 * types in {@code Foundgine.Core}.
 */
public record ToolCallGovernanceResult(PolicyDecision.PolicyOutcome outcome, String reason, TaskContract contract,
		ApprovalRequest pendingApproval) {
}
