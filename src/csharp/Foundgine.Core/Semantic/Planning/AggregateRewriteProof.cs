using Foundgine.Core.Semantic.Aggregates;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// The composite, fail-closed proof gate for a rewrite that substitutes one aggregate for
/// another (including substituting an aggregate for itself against a different candidate plan
/// or provider).
///
/// A rewrite of this shape touches four independent dimensions, and every one of them must be
/// satisfied before the rewrite is allowed to fire:
/// <list type="bullet">
///   <item>provider-neutral semantic equivalence of the surrounding plan
///     (<see cref="SemanticEquivalence"/>, via <see cref="SemanticEquivalenceProof"/>)</item>
///   <item>aggregate semantics — empty-collection result, NULL-input behavior, duplicate
///     sensitivity, and any cardinality requirement — preserved by the substitution
///     (<see cref="EmptySetEquivalence"/>, <see cref="NullEquivalence"/>,
///     <see cref="DuplicateEquivalence"/>, <see cref="CardinalityProof"/>, via
///     <see cref="AggregateRewriteLegality"/>)</item>
///   <item>the target provider actually declares support for the resulting aggregate
///     (via <see cref="AggregateProviderCapability"/>)</item>
///   <item>the rewrite does not regress the plan's security contract
///     (<see cref="AuthorizationPreservation"/>, via <see cref="AuthorizationPreservationProof"/>)</item>
/// </list>
/// <see cref="Create"/> fails closed: it throws <see cref="InvalidOperationException"/> the
/// moment any dimension is violated, rather than returning a proof callers might forget to
/// check. Semantic equivalence is checked first because nothing else about the rewrite is
/// meaningful if the rewritten plan does not even mean the same thing.
/// </summary>
public sealed record AggregateRewriteProof(
    SemanticEquivalenceProof SemanticEquivalence,
    AggregateRewriteLegalityResult EmptySetEquivalence,
    AggregateRewriteLegalityResult NullEquivalence,
    AggregateRewriteLegalityResult DuplicateEquivalence,
    AggregateRewriteLegalityResult CardinalityProof,
    SemanticFilterAggregate TargetAggregate,
    AggregateProviderCapability ProviderCapability,
    ProviderCostEstimate CostEstimate,
    AuthorizationPreservationProof AuthorizationPreservation)
{
    public bool IsSatisfied =>
        SemanticEquivalence.IsSatisfied
        && EmptySetEquivalence.IsLegal
        && NullEquivalence.IsLegal
        && DuplicateEquivalence.IsLegal
        && CardinalityProof.IsLegal
        && ProviderCapability.Supports(TargetAggregate)
        && AuthorizationPreservation.IsSatisfied;

    /// <summary>
    /// Builds and validates the full proof for substituting aggregate <paramref name="to"/> for
    /// aggregate <paramref name="from"/> when rewriting <paramref name="before"/> into
    /// <paramref name="after"/> under <paramref name="providerCapability"/>.
    /// </summary>
    public static AggregateRewriteProof Create(
        SemanticPlan before,
        SemanticPlan after,
        SemanticAggregateSemantics from,
        SemanticAggregateSemantics to,
        AggregateCardinalityProof cardinalityProof,
        AggregateProviderCapability providerCapability,
        ProviderCostEstimate costEstimate)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(cardinalityProof);
        ArgumentNullException.ThrowIfNull(providerCapability);

        // Nothing else about the rewrite matters if the rewritten plan doesn't even mean the
        // same thing as the source plan. This throws on violation, so it is checked first.
        var semanticEquivalence = SemanticEquivalenceProof.Create(before, after);

        // A rewrite can be semantically legal everywhere and still be un-executable on a
        // specific provider that never declared it can evaluate the resulting aggregate.
        if (!providerCapability.Supports(to.Aggregate))
            throw new InvalidOperationException(
                $"Provider '{providerCapability.ProviderName}' does not declare support for " +
                $"aggregate '{to.Aggregate}'.");

        var emptySetEquivalence = AggregateRewriteLegality.CheckEmptySemantics(from, to);
        var nullEquivalence = AggregateRewriteLegality.CheckNullSemantics(from, to);
        var duplicateEquivalence = AggregateRewriteLegality.CheckDuplicateSensitivity(from, to);
        var cardinalityResult = AggregateRewriteLegality.CheckCardinalityRequirement(
            from, to, cardinalityProof.Knowledge);

        var combinedLegality = AggregateRewriteLegalityResult.Combine(
            emptySetEquivalence, nullEquivalence, duplicateEquivalence, cardinalityResult);

        if (!combinedLegality.IsLegal)
            throw new InvalidOperationException(
                "Aggregate rewrite rejected because it does not preserve aggregate semantics: " +
                string.Join(" ", combinedLegality.Violations));

        var authorizationPreservation = AuthorizationPreservationProof.Create(before, after);

        if (!authorizationPreservation.IsSatisfied)
            throw new InvalidOperationException(
                "Aggregate rewrite rejected because it does not preserve the source plan's " +
                "security contract: " + string.Join(" ", authorizationPreservation.Violations));

        return new AggregateRewriteProof(
            semanticEquivalence,
            emptySetEquivalence,
            nullEquivalence,
            duplicateEquivalence,
            cardinalityResult,
            to.Aggregate,
            providerCapability,
            costEstimate,
            authorizationPreservation);
    }
}
