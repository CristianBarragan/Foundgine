package com.foundgine.runtime;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.semantic.SemanticRequest;
import com.foundgine.core.semantic.intent.ReadIntent;

import java.util.concurrent.CompletionStage;

/**
 * Port of {@code Foundgine.Runtime.IFoundgineExecutor}.
 *
 * <p>
 * Stable application-facing entry point for semantic execution.
 *
 * <p>
 * C#'s {@code ExecutionContext? context = null, CancellationToken
 * cancellationToken = default} optional parameters are ported as a
 * four-overload pair per method, matching this port's established convention
 * for optional-parameter C# methods.
 */
public interface IFoundgineExecutor {

	CompletionStage<ExecutionResult> executeAsync(SemanticRequest request, ExecutionContext context,
			CancellationToken cancellationToken);

	default CompletionStage<ExecutionResult> executeAsync(SemanticRequest request, ExecutionContext context) {
		return executeAsync(request, context, CancellationToken.NONE);
	}

	default CompletionStage<ExecutionResult> executeAsync(SemanticRequest request) {
		return executeAsync(request, null, CancellationToken.NONE);
	}

	/**
	 * Executes external, provider-neutral read intent after compiling it into the
	 * canonical semantic request. This overload is intended for adapters such as
	 * JSON APIs and AI tools.
	 */
	CompletionStage<ExecutionResult> executeAsync(ReadIntent intent, ExecutionContext context,
			CancellationToken cancellationToken);

	default CompletionStage<ExecutionResult> executeAsync(ReadIntent intent, ExecutionContext context) {
		return executeAsync(intent, context, CancellationToken.NONE);
	}

	default CompletionStage<ExecutionResult> executeAsync(ReadIntent intent) {
		return executeAsync(intent, null, CancellationToken.NONE);
	}
}
