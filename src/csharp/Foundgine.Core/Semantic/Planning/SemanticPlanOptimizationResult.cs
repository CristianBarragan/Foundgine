namespace Foundgine.Core.Semantic.Planning;

/// <summary>Result of provider-neutral semantic plan optimization.</summary>
public sealed record SemanticPlanOptimizationResult(
    SemanticPlan Plan,
    IReadOnlyList<string> AppliedRules,
    SecurityPreservationProof SecurityProof,
    SemanticEquivalenceProof SemanticProof,
    SemanticPlanAuthorizationBindingProof AuthorizationBindingProof,
    IReadOnlyList<PlanRewriteRuleResult>? RuleApplications = null,
    double TotalCostImpact = 0d,
    bool TerminatedNormally = true)
{
    public bool Changed => AppliedRules.Count != 0;
    public IReadOnlyList<PlanRewriteRuleResult> Applications => RuleApplications ?? [];
}