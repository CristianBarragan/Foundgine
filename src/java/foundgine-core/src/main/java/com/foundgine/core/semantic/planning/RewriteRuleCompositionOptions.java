package com.foundgine.core.semantic.planning;

/**
 * Safety, determinism and selection limits for composing semantic rewrite
 * rules.
 */
public record RewriteRuleCompositionOptions(int maxRuleApplications, int maxPlanVisits,
		RuleSelectionPolicy selectionPolicy, IProviderCostEstimator providerCostEstimator,
		ProviderCostSelectionPolicy providerCostSelectionPolicy) {
	public RewriteRuleCompositionOptions() {
		this(32, 64, null, null, null);
	}

	public RewriteRuleCompositionOptions(int maxRuleApplications, int maxPlanVisits) {
		this(maxRuleApplications, maxPlanVisits, null, null, null);
	}

	public RewriteRuleCompositionOptions validate() {
		if (maxRuleApplications < 1)
			throw new IllegalArgumentException("maxRuleApplications must be positive.");
		if (maxPlanVisits < 1)
			throw new IllegalArgumentException("maxPlanVisits must be positive.");
		(selectionPolicy == null ? new RuleSelectionPolicy() : selectionPolicy).validate();
		(providerCostSelectionPolicy == null ? new ProviderCostSelectionPolicy() : providerCostSelectionPolicy)
				.validate();
		if (providerCostEstimator != null
				&& (providerCostEstimator.provider() == null || providerCostEstimator.provider().isBlank()))
			throw new IllegalArgumentException("Provider cost estimator must identify its provider.");
		return this;
	}
}
