package com.foundgine.providers.gateway.execution;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.mutation.SemanticMutationOperationGraph;
import com.foundgine.runtime.MutationDryRunResult;
import com.foundgine.runtime.MutationExecutionResult;
import com.foundgine.runtime.MutationPlanApproval;
import java.util.concurrent.CompletionStage;

/**
 * Gateway-facing execution facade. It accepts Foundgine's provider-neutral intent contracts and delegates all
 * planning, authorization, warrant verification and provider execution to the existing runtime engines.
 * The gateway is not an alternate execution engine.
 */
public interface IFoundgineGatewayExecution {
    CompletionStage<ExecutionResult> executeReadAsync(ReadIntent intent, ExecutionContext context, CancellationToken cancellationToken);
    default CompletionStage<ExecutionResult> executeReadAsync(ReadIntent intent) {
        return executeReadAsync(intent, null, CancellationToken.NONE);
    }

    MutationDryRunResult dryRunMutation(SemanticMutationOperationGraph graph);

    MutationPlanApproval approveMutation(SemanticMutationOperationGraph graph);

    CompletionStage<MutationExecutionResult> executeMutationAsync(SemanticMutationOperationGraph graph,
            ExecutionContext context, CancellationToken cancellationToken);
    default CompletionStage<MutationExecutionResult> executeMutationAsync(SemanticMutationOperationGraph graph) {
        return executeMutationAsync(graph, null, CancellationToken.NONE);
    }

    CompletionStage<MutationExecutionResult> executeApprovedMutationAsync(MutationPlanApproval approval,
            ExecutionContext context, CancellationToken cancellationToken);
    default CompletionStage<MutationExecutionResult> executeApprovedMutationAsync(MutationPlanApproval approval) {
        return executeApprovedMutationAsync(approval, null, CancellationToken.NONE);
    }
}
