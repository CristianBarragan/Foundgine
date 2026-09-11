namespace Foundgine.Core.Semantic.Planning;

/// <summary>Auditable rewrite candidate including provider-specific execution cost.</summary>
public sealed record ProviderAwareRewriteRuleCandidate(
    string RuleName,
    string Provider,
    double BenefitEstimate,
    double RewriteCost,
    double EstimatedExecutionCost,
    double EstimatedRows,
    double CostConfidence,
    double Score,
    int Priority);