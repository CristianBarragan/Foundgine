package com.foundgine.runtime;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.semantic.SemanticRequest;
import com.foundgine.core.semantic.SemanticVersionSet;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationCapabilities;
import com.foundgine.core.semantic.capabilities.SemanticCapabilityContract;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;

import java.util.concurrent.CompletionStage;

/**
 * Port of {@code Foundgine.Runtime.IFoundgine}.
 *
 * <p>
 * Full application-facing Foundgine surface. Most application code can depend
 * on {@link IFoundgineExecutor} and use only {@code executeAsync}.
 *
 * <p>
 * C#'s {@code ExecutionContext? context = null, CancellationToken
 * cancellationToken = default} optional parameters on {@code
 * ExecuteApprovedAsync} are ported as the same overload pair used elsewhere in
 * this port.
 */
public interface IFoundgine extends IFoundgineExecutor {

	/**
	 * Describes the domain capabilities available under the configured
	 * authorization policy. This is discovery context, not an authorization
	 * decision cache; execution evaluates the policy again.
	 */
	SemanticAuthorizationCapabilities describeCapabilities();

	/** Returns the canonical machine-readable semantic capability contract. */
	SemanticCapabilityContract describeCapabilityContract();

	/**
	 * Returns the capability contract visible to a verified warrant-backed caller.
	 * Discovery never consumes replay state; execution still re-authorizes.
	 */
	SemanticCapabilityContract describeCapabilityContract(SecurityExecutionContext security);

	/** Returns the semantic compatibility versions used by this engine. */
	SemanticVersionSet describeVersionSet();

	/** Plans and authorizes a request without executing provider work. */
	DryRunResult dryRun(SemanticRequest request);

	/** Creates an approval bound to the exact currently authorized plan. */
	PlanApproval approvePlan(SemanticRequest request, String approvedBy);

	/**
	 * Executes only when the current authorized plan exactly matches the approval
	 * fingerprint.
	 */
	CompletionStage<ExecutionResult> executeApprovedAsync(PlanApproval approval, ExecutionContext context,
			CancellationToken cancellationToken);

	default CompletionStage<ExecutionResult> executeApprovedAsync(PlanApproval approval, ExecutionContext context) {
		return executeApprovedAsync(approval, context, CancellationToken.NONE);
	}

	default CompletionStage<ExecutionResult> executeApprovedAsync(PlanApproval approval) {
		return executeApprovedAsync(approval, null, CancellationToken.NONE);
	}
}
