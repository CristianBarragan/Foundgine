package com.foundgine.core.semantic.planning;

import java.util.Objects;

/** Combined deterministic score for a rewrite under a provider cost model. */
public record ProviderAwareRewriteScore(double value) {
	public static ProviderAwareRewriteScore calculate(RewriteBenefit benefit, RewriteCost rewriteCost,
			ProviderCostEstimate providerCost, ProviderCostSelectionPolicy policy) {
		Objects.requireNonNull(policy);
		policy.validate();
		double providerPenalty = policy.preferLowerProviderCost()
				? providerCost.estimatedExecutionCost() * policy.providerCostWeight()
				: 0d;
		return new ProviderAwareRewriteScore(
				benefit.estimatedBenefit() / (1d + rewriteCost.estimatedWork() + providerPenalty));
	}
}
