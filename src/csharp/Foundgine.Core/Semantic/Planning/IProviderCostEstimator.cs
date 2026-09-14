namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Provider-specific advisory cost model. Implementations may inspect a
/// candidate semantic plan and provider statistics/capabilities, but must not
/// change the semantic or security contract.
/// </summary>
public interface IProviderCostEstimator
{
    string Provider { get; }

    ProviderCostEstimate Estimate(
        SemanticPlan before,
        SemanticPlan candidate,
        IPlanRewriteRule rule);
}
