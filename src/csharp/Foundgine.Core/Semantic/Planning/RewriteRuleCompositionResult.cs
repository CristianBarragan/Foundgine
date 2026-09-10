namespace Foundgine.Core.Semantic.Planning;

/// <summary>Auditable result of a composed rewrite sequence.</summary>
public sealed record RewriteRuleCompositionResult(
    SemanticPlan Plan,
    IReadOnlyList<PlanRewriteRuleResult> Applications,
    double TotalCostImpact,
    bool TerminatedNormally,
    IReadOnlyList<RewriteRuleCandidate>? SelectionHistory = null,
    IReadOnlyList<ProviderAwareRewriteRuleCandidate>? ProviderSelectionHistory = null)
{
    public IReadOnlyList<string> AppliedRules => Applications.Select(x => x.RuleName).ToArray();
    public bool Changed => Applications.Count != 0;
    public IReadOnlyList<RewriteRuleCandidate> Candidates => SelectionHistory ?? [];
    public IReadOnlyList<ProviderAwareRewriteRuleCandidate> ProviderCandidates => ProviderSelectionHistory ?? [];
}