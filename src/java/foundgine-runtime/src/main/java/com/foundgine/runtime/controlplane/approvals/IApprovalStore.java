package com.foundgine.runtime.controlplane.approvals;

import java.util.Optional;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.Approvals.IApprovalStore}.
 *
 * <p>
 * <b>Porting decision:</b> the C# member
 * {@code bool TryGet(string, out ApprovalRequest?)} has no direct Java
 * equivalent (Java has no {@code out} parameters). It is ported as
 * {@link #tryGet(String)} returning {@code Optional<ApprovalRequest>}; an empty
 * {@code Optional} corresponds to the C# method returning {@code false}.
 */
public interface IApprovalStore {
	ApprovalRequest create(String requestFingerprint, int requiredApprovals);

	default ApprovalRequest create(String requestFingerprint) {
		return create(requestFingerprint, 1);
	}

	Optional<ApprovalRequest> tryGet(String approvalId);

	ApprovalRequest recordDecision(String approvalId, ApprovalRequest.ApprovalGrant grant);
}
