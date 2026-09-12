package com.foundgine.core.execution.mutation;

import java.util.List;

/**
 * Port of {@code Foundgine.Core.Execution.Mutation.ProviderMutationBatchPlan}
 * (a C# {@code abstract record} with one positional parameter).
 *
 * <p>
 * Opaque provider batch plan. Ordering, dependencies, and physical
 * representation belong to the provider-specific plan.
 *
 * <p>
 * Ported as an {@code abstract class} rather than an abstract record for the
 * same reason as {@link ProviderMutationPlan}: Java records cannot be extended,
 * and a subtype should get equality over its own full shape, not just this one
 * field.
 */
public abstract class ProviderMutationBatchPlan {

	private final List<ProviderMutationPlan> operations;

	protected ProviderMutationBatchPlan(List<ProviderMutationPlan> operations) {
		this.operations = operations;
	}

	public List<ProviderMutationPlan> operations() {
		return operations;
	}
}
