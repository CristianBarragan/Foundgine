package com.foundgine.runtime.api;

import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.mutation.MutationBatchResult;
import com.foundgine.core.semantic.mutation.*;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import java.time.OffsetDateTime;
import java.util.*;
import java.util.concurrent.CompletableFuture;

public interface IFoundgineMutations {
	MutationDryRunResult dryRun(SemanticMutationRequest request);

	CompletableFuture<MutationExecutionResult> executeAsync(SemanticMutationRequest request, ExecutionContext context);

	MutationPlanApproval approve(SemanticMutationRequest request, String approvedBy);

	CompletableFuture<MutationExecutionResult> executeApprovedAsync(MutationPlanApproval approval,
			ExecutionContext context);

	record SemanticMutationRequest(SemanticMutationOperationGraph graph, SecurityExecutionContext security) {
		public SemanticMutationRequest(SemanticMutationOperationGraph graph) {
			this(graph, null);
		}
	}

	record MutationDryRunResult(String planFingerprint, List<MutationPlanOperation> operations, List<String> effects) {
	}

	record MutationPlanOperation(int index, String entity, String kind, List<String> fields,
			List<String> returnFields) {
	}

	record MutationPlanApproval(SemanticMutationRequest request, String approvalId, String planFingerprint,
			String approvedBy, OffsetDateTime approvedAt) {
	}

	record MutationExecutionResult(MutationBatchResult result, String planFingerprint, String resultFingerprint,
			String approvalId, String approvedBy) {
	}
}
