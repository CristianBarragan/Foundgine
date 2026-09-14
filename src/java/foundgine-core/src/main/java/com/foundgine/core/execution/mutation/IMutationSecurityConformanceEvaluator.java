package com.foundgine.core.execution.mutation;

/**
 * Port of
 * {@code Foundgine.Core.Execution.Mutation.IMutationSecurityConformanceEvaluator}.
 *
 * <p>
 * Provider-specific evaluation of the actual mutation execution representation.
 * Implementations must report only guarantees they can establish for their
 * concrete execution path.
 */
public interface IMutationSecurityConformanceEvaluator {
	MutationSecurityConformanceResult evaluate(ExecutionMutationIR ir);
}
