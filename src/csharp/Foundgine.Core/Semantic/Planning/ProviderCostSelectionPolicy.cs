namespace Foundgine.Core.Semantic.Planning;

/// <summary>Controls how provider execution cost participates in rewrite ranking.</summary>
public sealed record ProviderCostSelectionPolicy(
    bool PreferLowerProviderCost = true,
    double ProviderCostWeight = 1d)
{
    public ProviderCostSelectionPolicy Validate()
    {
        if (double.IsNaN(ProviderCostWeight) || double.IsInfinity(ProviderCostWeight) || ProviderCostWeight < 0d)
            throw new ArgumentOutOfRangeException(nameof(ProviderCostWeight));
        return this;
    }
}
