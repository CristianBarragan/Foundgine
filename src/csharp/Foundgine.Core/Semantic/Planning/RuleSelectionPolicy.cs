namespace Foundgine.Core.Semantic.Planning;

/// <summary>Policy controlling deterministic selection among simultaneously applicable rewrite rules.</summary>
public sealed record RuleSelectionPolicy(
    bool PreferHigherBenefit = true,
    bool PenalizeRewriteCost = true,
    double MinimumScore = double.NegativeInfinity)
{
    public RuleSelectionPolicy Validate()
    {
        if (double.IsNaN(MinimumScore) || double.IsPositiveInfinity(MinimumScore))
            throw new ArgumentOutOfRangeException(nameof(MinimumScore));
        return this;
    }

    public RewriteScore Score(IPlanRewriteRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var benefit = RewriteBenefit.From(Math.Max(0d, rule.BenefitEstimate));
        var cost = RewriteCost.From(Math.Max(0d, rule.CostImpact));
        if (!PenalizeRewriteCost)
            return new RewriteScore(PreferHigherBenefit ? benefit.EstimatedBenefit : -benefit.EstimatedBenefit);
        var score = RewriteScore.Calculate(benefit, cost).Value;
        return new RewriteScore(PreferHigherBenefit ? score : -score);
    }
}
