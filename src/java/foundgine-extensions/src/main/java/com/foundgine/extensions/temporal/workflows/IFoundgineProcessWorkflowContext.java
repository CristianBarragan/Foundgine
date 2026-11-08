package com.foundgine.extensions.temporal.workflows;

import com.foundgine.extensions.temporal.activities.FoundgineActivityFailureException;
import com.foundgine.extensions.temporal.contracts.FoundgineActivityPolicy;
import com.foundgine.extensions.temporal.contracts.FoundgineOperationRequest;
import com.foundgine.extensions.temporal.contracts.FoundgineOperationResult;
import java.time.Duration;
import java.util.function.BooleanSupplier;

/**
 * The only way workflow code touches the outside world, so the same {@link FoundgineProcessWorkflow} runs on the
 * in-process reference engine or behind a Temporal SDK adapter (map these two calls to {@code ExecuteActivityAsync}
 * and {@code WaitConditionAsync}). Workflow code stays deterministic: no I/O, no clocks, no service calls.
 */
public interface IFoundgineProcessWorkflowContext {
    /**
     * Executes the operation activity with the retry and timeout rules of the policy.
     *
     * @param cancellable when false the call ignores workflow cancellation (compensation must still run)
     * @throws FoundgineActivityFailureException once retries are exhausted or the failure is non-retryable
     * @throws FoundgineWorkflowCancelledException when {@code cancellable} and the workflow is cancelled
     */
    FoundgineOperationResult executeActivity(FoundgineOperationRequest request, FoundgineActivityPolicy policy,
            boolean cancellable);

    /**
     * Blocks until the condition holds or the timeout elapses.
     *
     * @param timeout null for no timeout
     * @return true when the condition held, false on timeout
     * @throws FoundgineWorkflowCancelledException when the workflow is cancelled while waiting
     */
    boolean waitCondition(BooleanSupplier condition, Duration timeout);
}
