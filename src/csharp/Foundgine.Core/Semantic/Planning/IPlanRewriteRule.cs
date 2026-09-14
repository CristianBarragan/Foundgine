namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Contract for one provider-neutral semantic plan rewrite.
/// Rules are composable: they declare ordering constraints, conflicts,
/// idempotence and cost so the optimizer can build a deterministic rewrite path.
/// Every accepted application is independently checked for semantic and
/// security preservation.
/// </summary>
public interface IPlanRewriteRule
{
    string Name { get; }

    IReadOnlyList<string> Preconditions { get; }

    IReadOnlyList<string> SecurityObligations { get; }

    double CostImpact { get; }

    /// <summary>Estimated provider-neutral execution benefit used for candidate selection.</summary>
    double BenefitEstimate => 0d;

    /// <summary>Rules that must have been applied before this rule may run.</summary>
    IReadOnlyList<string> MustRunAfter => [];

    /// <summary>Rules that must run after this rule.</summary>
    IReadOnlyList<string> MustRunBefore => [];

    /// <summary>Rules that cannot be composed with this rule.</summary>
    IReadOnlyList<string> ConflictsWith => [];

    /// <summary>Whether applying the rule twice to an already-normalized result is unnecessary.</summary>
    bool IsIdempotent => true;

    /// <summary>Deterministic tie-breaker when multiple applicable rules are available.</summary>
    int Priority => 0;

    bool CanApply(SemanticPlan plan);

    SemanticPlan Apply(SemanticPlan plan);
}
