namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Applies a deterministic composition of provider-neutral rewrite rules.
/// Every accepted rule application is independently checked for semantic and
/// security preservation; composition additionally enforces ordering, conflicts,
/// idempotence and termination budgets.
/// </summary>
public sealed class SemanticPlanOptimizer : IPlanOptimizer
{
    private readonly RewriteRuleComposer _composer;

    public SemanticPlanOptimizer(
        IEnumerable<IPlanRewriteRule>? rules = null,
        RewriteRuleCompositionOptions? options = null)
    {
        _composer = new RewriteRuleComposer(
            rules ??
            [
                new AuthorizationCanonicalizationRule(), new PredicatePushdownRule(), new ProjectionPruningRule(),
                new RelationshipTraversalOptimizationRule(), new RelationshipJoinOrderingRule(),
                new AggregateRelationshipFilterPushdownRule(), new AggregateCardinalityOptimizationRule()
            ],
            options);
    }

    public SemanticPlanOptimizationResult Optimize(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var composition = _composer.Compose(plan);

        var securityProof = composition.Applications.Count == 0
            ? SecurityPreservationProof.Create(plan, composition.Plan)
            : SecurityPreservationProof.Create(plan, composition.Plan);
        var semanticProof = SemanticEquivalenceProof.Create(plan, composition.Plan);
        var authorizationBindingProof = SemanticPlanAuthorizationBindingProof.Create(plan, composition.Plan);

        return new SemanticPlanOptimizationResult(
            composition.Plan,
            composition.AppliedRules,
            securityProof,
            semanticProof,
            authorizationBindingProof,
            composition.Applications,
            composition.TotalCostImpact,
            composition.TerminatedNormally);
    }

    public static PlanRewriteRuleResult ApplyRule(IPlanRewriteRule rule, SemanticPlan before, SemanticPlan after)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var securityProof = SecurityPreservationProof.Create(before, after);
        var obligationProof = SecurityObligationProof.Create(rule, before, after);
        var semanticProof = SemanticEquivalenceProof.Create(before, after);
        var authorizationBindingProof = SemanticPlanAuthorizationBindingProof.Create(before, after);
        var optimizationProof = PlanOptimizationProof.Create(rule, before, after);
        return new PlanRewriteRuleResult(
            rule.Name,
            before,
            after,
            rule.Preconditions,
            rule.SecurityObligations,
            rule.CostImpact,
            securityProof,
            obligationProof,
            semanticProof,
            authorizationBindingProof,
            optimizationProof);
    }
}