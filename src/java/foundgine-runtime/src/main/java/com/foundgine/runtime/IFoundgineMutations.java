package com.foundgine.runtime;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;

import java.util.concurrent.CompletionStage;

/**
 * Port of {@code Foundgine.Runtime.IFoundgineMutations}.
 *
 * <p>
 * C#'s {@code ExecutionContext? context = null, CancellationToken
 * cancellationToken = default} optional parameters are ported as the same
 * overload pair used throughout this port.
 */
public interface IFoundgineMutations {

	MutationDryRunResult dryRun(SemanticMutationRequest request);

	CompletionStage<MutationExecutionResult> executeAsync(SemanticMutationRequest request, ExecutionContext context,
			CancellationToken cancellationToken);

	default CompletionStage<MutationExecutionResult> executeAsync(SemanticMutationRequest request,
			ExecutionContext context) {
		return executeAsync(request, context, CancellationToken.NONE);
	}

	default CompletionStage<MutationExecutionResult> executeAsync(SemanticMutationRequest request) {
		return executeAsync(request, null, CancellationToken.NONE);
	}

	MutationPlanApproval approve(SemanticMutationRequest request, String approvedBy);

	CompletionStage<MutationExecutionResult> executeApprovedAsync(MutationPlanApproval approval,
			ExecutionContext context, CancellationToken cancellationToken);

	default CompletionStage<MutationExecutionResult> executeApprovedAsync(MutationPlanApproval approval,
			ExecutionContext context) {
		return executeApprovedAsync(approval, context, CancellationToken.NONE);
	}

	default CompletionStage<MutationExecutionResult> executeApprovedAsync(MutationPlanApproval approval) {
		return executeApprovedAsync(approval, null, CancellationToken.NONE);
	}
}
