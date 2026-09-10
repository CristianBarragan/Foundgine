using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Aggregates;

/// <summary>
/// What a provider declares it can execute for aggregate-based rewrites: which aggregate
/// functions it can evaluate, and whether it can render aggregate predicates and relationship
/// quantifiers at all.
///
/// This is a declared contract, not an inferred one. A rewrite must never assume a provider
/// can execute an aggregate just because it looks like a common SQL-ish operation — the
/// provider has to say so. This is what lets <see cref="AggregateRewriteLegality"/>'s
/// semantic-level legality gate be paired with a provider-level capability gate, so a rewrite
/// that is semantically legal everywhere can still be rejected for a specific provider that
/// never declared support for the resulting aggregate.
/// </summary>
public sealed record AggregateProviderCapability(
    string ProviderName,
    IReadOnlyList<SemanticFilterAggregate> SupportedAggregates,
    bool SupportsAggregatePredicate,
    bool SupportsRelationshipQuantifiers)
{
    /// <summary>Whether this provider declares support for evaluating <paramref name="aggregate"/>.</summary>
    public bool Supports(SemanticFilterAggregate aggregate) => SupportedAggregates.Contains(aggregate);
}

/// <summary>
/// Central registry of known provider aggregate capabilities. Rewrite rules and proofs should
/// consult this registry (or an equivalent capability supplied by the caller) rather than
/// assuming a provider's capabilities implicitly.
/// </summary>
public static class AggregateProviderCapabilityRegistry
{
    /// <summary>
    /// The generic SQL provider: every catalogued aggregate is supported, along with aggregate
    /// predicates and relationship quantifiers.
    /// </summary>
    public static readonly AggregateProviderCapability GenericSql = new(
        "sql",
        [SemanticFilterAggregate.Count, SemanticFilterAggregate.Min, SemanticFilterAggregate.Max],
        SupportsAggregatePredicate: true,
        SupportsRelationshipQuantifiers: true);
}