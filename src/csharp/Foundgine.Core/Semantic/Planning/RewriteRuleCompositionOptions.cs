namespace Foundgine.Core.Semantic.Planning;

/// <summary>Safety, determinism and selection limits for composing semantic rewrite rules.</summary>
public sealed record RewriteRuleCompositionOptions(
    int MaxRuleApplications = 32,
    int MaxPlanVisits = 64,
    RuleSelectionPolicy? SelectionPolicy = null,
    IProviderCostEstimator? ProviderCostEstimator = null,
    ProviderCostSelectionPolicy? ProviderCostSelectionPolicy = null)
{
    public RewriteRuleCompositionOptions Validate()
    {
        if (MaxRuleApplications < 1) throw new ArgumentOutOfRangeException(nameof(MaxRuleApplications));
        if (MaxPlanVisits < 1) throw new ArgumentOutOfRangeException(nameof(MaxPlanVisits));
        (SelectionPolicy ?? new RuleSelectionPolicy()).Validate();
        (ProviderCostSelectionPolicy ?? new ProviderCostSelectionPolicy()).Validate();
        if (ProviderCostEstimator is not null && string.IsNullOrWhiteSpace(ProviderCostEstimator.Provider))
            throw new ArgumentException("Provider cost estimator must identify its provider.",
                nameof(ProviderCostEstimator));
        return this;
    }
}