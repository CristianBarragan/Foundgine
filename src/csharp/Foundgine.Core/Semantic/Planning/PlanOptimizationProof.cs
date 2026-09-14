namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Records the admissibility proof for a semantic-plan optimization.
/// An optimization is admissible only when semantic meaning, security
/// obligations, and authorization binding are preserved. Benefit/cost values
/// are advisory selection inputs; this proof deliberately does not pretend
/// that a provider-neutral estimate is a measured execution cost.
/// </summary>
public sealed record PlanOptimizationProof(
    string RuleName,
    bool SemanticMeaningPreserved,
    bool SecurityPreserved,
    bool AuthorizationBindingPreserved,
    double EstimatedBenefit,
    double EstimatedRewriteCost)
{
    public bool IsSatisfied =>
        SemanticMeaningPreserved &&
        SecurityPreserved &&
        AuthorizationBindingPreserved &&
        EstimatedBenefit >= 0d &&
        EstimatedRewriteCost >= 0d;

    public static PlanOptimizationProof Create(
        IPlanRewriteRule rule,
        SemanticPlan before,
        SemanticPlan after)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        // Reuse the canonical proofs rather than introducing a second
        // interpretation of semantic or security equivalence.
        var semantic = SemanticEquivalenceProof.Create(before, after);
        var security = SecurityPreservationProof.Create(before, after);
        var authorization = SemanticPlanAuthorizationBindingProof.Create(before, after);

        var proof = new PlanOptimizationProof(
            rule.Name,
            semantic.IsSatisfied,
            security.IsSatisfied,
            authorization.IsSatisfied,
            Math.Max(0d, rule.BenefitEstimate),
            Math.Max(0d, rule.CostImpact));

        if (!proof.IsSatisfied)
            throw new InvalidOperationException(
                $"Optimization '{rule.Name}' is not admissible because one or more preservation obligations failed.");

        return proof;
    }
}