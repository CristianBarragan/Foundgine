namespace Foundgine.Core.Semantic.Planning;

/// <summary>Auditable candidate considered by the rewrite selector.</summary>
public sealed record RewriteRuleCandidate(
    string RuleName,
    double BenefitEstimate,
    double CostImpact,
    double Score,
    int Priority);
