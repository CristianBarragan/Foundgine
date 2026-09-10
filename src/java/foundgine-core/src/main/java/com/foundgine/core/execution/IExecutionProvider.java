package com.foundgine.core.execution;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;

/** Executes an already-compiled provider plan. */
public interface IExecutionProvider {
    CompletionStage<ExecutionResult> executeAsync(
            ProviderPlan plan,
            ExecutionContext context,
            CancellationToken cancellationToken);

    default CompletionStage<ExecutionResult> executeAsync(
            ProviderPlan plan,
            ExecutionContext context) {
        return executeAsync(plan, context, CancellationToken.NONE);
    }
}
