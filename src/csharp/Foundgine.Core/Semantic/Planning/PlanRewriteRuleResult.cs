namespace Foundgine.Core.Semantic.Planning;

/// <summary>Auditable result of applying one rewrite rule.</summary>
public sealed record PlanRewriteRuleResult(
    string RuleName,
    SemanticPlan Before,
    SemanticPlan After,
    IReadOnlyList<string> Preconditions,
    IReadOnlyList<string> SecurityObligations,
    double CostImpact,
    SecurityPreservationProof SecurityProof,
    SecurityObligationProof SecurityObligationProof,
    SemanticEquivalenceProof SemanticProof,
    SemanticPlanAuthorizationBindingProof AuthorizationBindingProof,
    PlanOptimizationProof? OptimizationProof = null)
{
    public bool IsSatisfied =>
        SecurityProof.IsSatisfied &&
        SecurityObligationProof.IsSatisfied &&
        SemanticProof.IsSatisfied &&
        AuthorizationBindingProof.IsSatisfied &&
        (OptimizationProof?.IsSatisfied ?? true);
}
