package com.foundgine.runtime;

import java.time.Instant;

/** Port of the {@code MutationPlanApproval} record declared alongside {@code IFoundgineMutations}. */
public record MutationPlanApproval(
        SemanticMutationRequest request,
        String approvalId,
        String planFingerprint,
        String approvedBy,
        Instant approvedAt) {
}
