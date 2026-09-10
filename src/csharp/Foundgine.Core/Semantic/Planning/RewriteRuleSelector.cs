namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Selects the best currently applicable rewrite candidate without bypassing
/// rule ordering, proof obligations, or deterministic tie-breaking.
/// </summary>
public sealed class RewriteRuleSelector
{
    private readonly RuleSelectionPolicy _policy;
    private readonly IProviderCostEstimator? _providerCostEstimator;
    private readonly ProviderCostSelectionPolicy _providerPolicy;

    public RewriteRuleSelector(
        RuleSelectionPolicy? policy = null,
        IProviderCostEstimator? providerCostEstimator = null,
        ProviderCostSelectionPolicy? providerPolicy = null)
    {
        _policy = (policy ?? new()).Validate();
        _providerCostEstimator = providerCostEstimator;
        _providerPolicy = (providerPolicy ?? new()).Validate();
    }

    public RewriteRuleCandidate? Select(
        SemanticPlan plan,
        IEnumerable<IPlanRewriteRule> candidates)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(candidates);

        var applicable = candidates
            .Where(rule => rule.CanApply(plan))
            .Select(rule => new RewriteRuleCandidate(
                rule.Name,
                rule.BenefitEstimate,
                rule.CostImpact,
                _policy.Score(rule).Value,
                rule.Priority))
            .Where(candidate => candidate.Score >= _policy.MinimumScore)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.RuleName, StringComparer.Ordinal)
            .ToArray();

        return applicable.FirstOrDefault();
    }

    /// <summary>
    /// Selects using provider-specific execution estimates. The provider model
    /// can influence ranking only; proof obligations remain enforced after the
    /// candidate is actually applied.
    /// </summary>
    public ProviderAwareRewriteRuleCandidate? SelectProviderAware(
        SemanticPlan plan,
        IEnumerable<IPlanRewriteRule> candidates)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(candidates);

        if (_providerCostEstimator is null)
            throw new InvalidOperationException("A provider cost estimator is required for provider-aware selection.");

        var applicable = candidates
            .Where(rule => rule.CanApply(plan))
            .Select(rule =>
            {
                var candidatePlan = rule.Apply(plan);
                var estimate = _providerCostEstimator.Estimate(plan, candidatePlan, rule);
                var benefit = RewriteBenefit.From(Math.Max(0d, rule.BenefitEstimate));
                var rewriteCost = RewriteCost.From(Math.Max(0d, rule.CostImpact));
                var score = ProviderAwareRewriteScore.Calculate(
                    benefit,
                    rewriteCost,
                    estimate,
                    _providerPolicy).Value;

                return new ProviderAwareRewriteRuleCandidate(
                    rule.Name,
                    estimate.Provider,
                    benefit.EstimatedBenefit,
                    rewriteCost.EstimatedWork,
                    estimate.EstimatedExecutionCost,
                    estimate.EstimatedRows,
                    estimate.Confidence,
                    score,
                    rule.Priority);
            })
            .Where(candidate => candidate.Score >= _policy.MinimumScore)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.RuleName, StringComparer.Ordinal)
            .ToArray();

        return applicable.FirstOrDefault();
    }
}