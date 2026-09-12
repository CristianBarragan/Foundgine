package com.foundgine.core.execution.mutation;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;

/**
 * Port of
 * {@code Foundgine.Core.Execution.Mutation.IMutationBatchExecutionProvider}.
 *
 * <p>
 * Executes an ordered mutation batch from the canonical provider-neutral
 * execution representation.
 */
public interface IMutationBatchExecutionProvider {

	MutationBatchResult executeBatch(ExecutionMutationIR ir, ExecutionContext context);

	/**
	 * Cancellation-aware execution boundary. Providers must propagate the token to
	 * their physical command and roll back any transaction they own when
	 * cancellation interrupts execution. The default preserves compatibility for
	 * providers that do not yet implement cancellation-aware execution.
	 */
	default MutationBatchResult executeBatch(ExecutionMutationIR ir, ExecutionContext context,
			CancellationToken cancellationToken) {
		cancellationToken.throwIfCancellationRequested();
		return executeBatch(ir, context);
	}
}
