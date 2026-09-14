using Foundgine.Core.Semantic.Aggregates;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class AggregateCardinalityProofTests
{
    [Fact]
    public void FromCardinality_one_proves_at_most_one()
    {
        var proof = AggregateCardinalityProof.FromCardinality(RelationshipCardinality.One);

        Assert.Equal(SemanticCardinalityKnowledge.AtMostOne, proof.Knowledge);
    }

    [Fact]
    public void FromCardinality_many_proves_unbounded()
    {
        var proof = AggregateCardinalityProof.FromCardinality(RelationshipCardinality.Many);

        Assert.Equal(SemanticCardinalityKnowledge.Unbounded, proof.Knowledge);
    }

    [Fact]
    public void Unknown_carries_no_cardinality_knowledge()
    {
        Assert.Equal(SemanticCardinalityKnowledge.Unknown, AggregateCardinalityProof.Unknown.Knowledge);
    }

    [Fact]
    public void Derived_knowledge_satisfies_the_legality_gate_when_a_requirement_exists()
    {
        var from = SemanticAggregateSemanticsCatalog.Min with
        {
            CardinalityRequirement = SemanticCardinalityRequirement.RequiresProof
        };
        var proof = AggregateCardinalityProof.FromCardinality(RelationshipCardinality.One);

        var result = AggregateRewriteLegality.CheckCardinalityRequirement(
            from, SemanticAggregateSemanticsCatalog.Max, proof.Knowledge);

        Assert.True(result.IsLegal);
    }

    [Fact]
    public void Unknown_still_fails_the_legality_gate_when_a_requirement_exists()
    {
        var from = SemanticAggregateSemanticsCatalog.Min with
        {
            CardinalityRequirement = SemanticCardinalityRequirement.RequiresProof
        };

        var result = AggregateRewriteLegality.CheckCardinalityRequirement(
            from, SemanticAggregateSemanticsCatalog.Max, AggregateCardinalityProof.Unknown.Knowledge);

        Assert.False(result.IsLegal);
    }
}
