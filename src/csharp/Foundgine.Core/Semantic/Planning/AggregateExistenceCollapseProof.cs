using Foundgine.Core.Semantic.Aggregates;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Fail-closed proof for collapsing a predicate-bearing COUNT comparison into a relationship
/// quantifier (SOME/NONE). Unlike aggregate substitution, this rewrite changes the surface
/// operation from an aggregate predicate to a relationship quantifier, so provider support for
/// relationship quantifiers is an explicit proof dimension.
/// </summary>
public sealed record AggregateExistenceCollapseProof(
    SemanticEquivalenceProof SemanticEquivalence,
    bool PredicateShapePreserved,
    bool ProviderCapabilitySatisfied,
    bool AuthorizationPreserved)
{
    public bool IsSatisfied =>
        SemanticEquivalence.IsSatisfied
        && PredicateShapePreserved
        && ProviderCapabilitySatisfied
        && AuthorizationPreserved;

    public static AggregateExistenceCollapseProof Create(
        SemanticPlan before,
        SemanticPlan after,
        AggregateProviderCapability providerCapability,
        bool predicateShapePreserved)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(providerCapability);

        var semanticEquivalence = SemanticEquivalenceProof.Create(before, after);

        if (!predicateShapePreserved)
            throw new InvalidOperationException(
                "Existence collapse rejected because the COUNT predicate shape was not preserved.");

        if (!providerCapability.SupportsRelationshipQuantifiers)
            throw new InvalidOperationException(
                $"Provider '{providerCapability.ProviderName}' does not declare support for relationship quantifiers.");

        var authorization = AuthorizationPreservationProof.Create(before, after);
        if (!authorization.IsSatisfied)
            throw new InvalidOperationException(
                "Existence collapse rejected because the source plan's security contract was not preserved: " +
                string.Join(" ", authorization.Violations));

        return new AggregateExistenceCollapseProof(
            semanticEquivalence,
            predicateShapePreserved,
            providerCapability.SupportsRelationshipQuantifiers,
            authorization.IsSatisfied);
    }
}