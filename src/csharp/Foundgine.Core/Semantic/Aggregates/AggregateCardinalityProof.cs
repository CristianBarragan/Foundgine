namespace Foundgine.Core.Semantic.Aggregates;

/// <summary>
/// Bridges a structurally-known <see cref="RelationshipCardinality"/> into the
/// <see cref="SemanticCardinalityKnowledge"/> vocabulary that <see cref="AggregateRewriteLegality"/>
/// consumes.
///
/// This is deliberately the only supported way to hand cardinality knowledge to a rewrite
/// proof. Without it, callers would be tempted to construct <see cref="SemanticCardinalityKnowledge"/>
/// values directly from ad-hoc reasoning about a relationship, which is exactly the kind of
/// implicit assumption <see cref="AggregateRewriteLegality"/> is meant to fail closed against.
/// Routing every cardinality claim through this type means "the model proved this relationship
/// has cardinality X" and "the legality check believes it knows X" can never silently drift
/// apart.
/// </summary>
public sealed record AggregateCardinalityProof(SemanticCardinalityKnowledge Knowledge)
{
    /// <summary>
    /// Nothing is known about relationship cardinality at rewrite time. Any rewrite that
    /// requires a cardinality proof will be rejected by <see cref="AggregateRewriteLegality"/>
    /// when this is supplied.
    /// </summary>
    public static AggregateCardinalityProof Unknown { get; } = new(SemanticCardinalityKnowledge.Unknown);

    /// <summary>
    /// Derives cardinality knowledge from a structurally-proven <see cref="RelationshipCardinality"/>:
    /// <see cref="RelationshipCardinality.One"/> proves at-most-one, and
    /// <see cref="RelationshipCardinality.Many"/> proves nothing more than "possibly more than one",
    /// i.e. unbounded.
    /// </summary>
    public static AggregateCardinalityProof FromCardinality(RelationshipCardinality cardinality) =>
        new(cardinality switch
        {
            RelationshipCardinality.One => SemanticCardinalityKnowledge.AtMostOne,
            RelationshipCardinality.Many => SemanticCardinalityKnowledge.Unbounded,
            _ => throw new ArgumentOutOfRangeException(nameof(cardinality), cardinality,
                "Unknown relationship cardinality.")
        });
}