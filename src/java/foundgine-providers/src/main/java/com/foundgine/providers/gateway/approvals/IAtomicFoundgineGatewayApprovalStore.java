package com.foundgine.providers.gateway.approvals;

import com.foundgine.runtime.MutationPlanApproval;

/**
 * Stronger approval-store contract: approval state and audit/outbox evidence must commit atomically
 * in one storage transaction. Implementations must not publish an approval if evidence persistence fails.
 */
public interface IAtomicFoundgineGatewayApprovalStore extends IFoundgineGatewayApprovalStore {
    void storeWithEvidence(String handleDigest, MutationPlanApproval approval,
            FoundgineGatewayApprovalEvidence evidence);
}
