namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Provider-specific estimate for executing a candidate semantic plan.
/// The estimate is advisory: it may influence selection, but it cannot weaken
/// semantic equivalence or security requirements.
/// </summary>
public readonly record struct ProviderCostEstimate(
    string Provider,
    double EstimatedExecutionCost,
    double EstimatedRows = 0d,
    double Confidence = 0.5d,
    CostEstimateProvenance? Provenance = null)
{
    public CostEstimateProvenance EffectiveProvenance => Provenance ?? CostEstimateProvenance.Heuristic();

    public static ProviderCostEstimate From(
        string provider,
        double estimatedExecutionCost,
        double estimatedRows = 0d,
        double confidence = 0.5d,
        CostEstimateProvenance? provenance = null)
    {
        if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("Provider is required.", nameof(provider));
        ValidateFiniteNonNegative(estimatedExecutionCost, nameof(estimatedExecutionCost));
        ValidateFiniteNonNegative(estimatedRows, nameof(estimatedRows));
        if (double.IsNaN(confidence) || double.IsInfinity(confidence) || confidence < 0d || confidence > 1d)
            throw new ArgumentOutOfRangeException(nameof(confidence));
        return new ProviderCostEstimate(provider, estimatedExecutionCost, estimatedRows, confidence, provenance);
    }

    private static void ValidateFiniteNonNegative(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            throw new ArgumentOutOfRangeException(name);
    }
}