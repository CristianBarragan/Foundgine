namespace Foundgine.Core.Semantic.Planning;

/// <summary>Combined deterministic score for a rewrite under a specific provider cost model.</summary>
public readonly record struct ProviderAwareRewriteScore(double Value)
{
    public static ProviderAwareRewriteScore Calculate(
        RewriteBenefit benefit,
        RewriteCost rewriteCost,
        ProviderCostEstimate providerCost,
        ProviderCostSelectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        var rewritePenalty = rewriteCost.EstimatedWork;
        var providerPenalty = policy.PreferLowerProviderCost
            ? providerCost.EstimatedExecutionCost * policy.ProviderCostWeight
            : 0d;
        var denominator = 1d + rewritePenalty + providerPenalty;
        var score = benefit.EstimatedBenefit / denominator;
        return new ProviderAwareRewriteScore(score);
    }
}
