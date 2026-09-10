package com.foundgine.core.execution.mutation;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;

/** Port of {@code Foundgine.Core.Execution.Mutation.IMutationExecutionProvider}. */
public interface IMutationExecutionProvider {

    MutationResult execute(ProviderMutationPlan plan, ExecutionContext context);

    /**
     * Cancellation-aware execution boundary. The default method mirrors the
     * C# default-interface-method overload; see {@link CancellationToken}
     * for the caveat that Java's cancellation model here is a placeholder.
     */
    default MutationResult execute(ProviderMutationPlan plan, ExecutionContext context, CancellationToken cancellationToken) {
        cancellationToken.throwIfCancellationRequested();
        return execute(plan, context);
    }
}
