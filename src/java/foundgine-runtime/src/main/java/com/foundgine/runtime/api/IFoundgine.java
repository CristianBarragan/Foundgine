package com.foundgine.runtime.api;

import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationCapabilities;
import com.foundgine.core.semantic.capabilities.*;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import java.util.concurrent.CompletableFuture;

public interface IFoundgine extends IFoundgineExecutor {
	SemanticAuthorizationCapabilities describeCapabilities();

	SemanticCapabilityContract describeCapabilityContract();

	SemanticCapabilityContract describeCapabilityContract(SecurityExecutionContext security);

	SemanticVersionSet describeVersionSet();

	DryRunResult dryRun(SemanticRequest request);

	PlanApproval approvePlan(SemanticRequest request, String approvedBy);

	CompletableFuture<ExecutionResult> executeApprovedAsync(PlanApproval approval, ExecutionContext context);

	default CompletableFuture<ExecutionResult> executeApprovedAsync(PlanApproval approval) {
		return executeApprovedAsync(approval, null);
	}
}
