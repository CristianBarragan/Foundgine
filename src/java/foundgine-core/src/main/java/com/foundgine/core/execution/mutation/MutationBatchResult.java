package com.foundgine.core.execution.mutation;

import java.util.List;

/** Port of {@code Foundgine.Core.Execution.Mutation.MutationBatchResult}: results from an atomic mutation batch, in execution order. */
public record MutationBatchResult(List<MutationResult> results) {

    public int totalAffectedRows() {
        return results.stream().mapToInt(MutationResult::affectedRows).sum();
    }
}
