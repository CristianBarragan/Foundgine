package com.foundgine.runtime;

import com.foundgine.core.execution.mutation.MutationBatchResult;

/** Port of the {@code MutationExecutionResult} record declared alongside {@code IFoundgineMutations}. */
public record MutationExecutionResult(
        MutationBatchResult result,
        String planFingerprint,
        String resultFingerprint,
        String approvalId,
        String approvedBy) {
}
