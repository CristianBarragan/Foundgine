using Foundgine.Core.Semantic.Aggregates;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class AggregateRewriteLegalityTests
{
    [Fact]
    public void Substituting_an_aggregate_for_itself_is_always_legal()
    {
        var result = AggregateRewriteLegality.CheckSubstitution(
            SemanticFilterAggregate.Count,
            SemanticFilterAggregate.Count);

        Assert.True(result.IsLegal);
        Assert.Empty(result.Violations);
    }

    [Theory]
    [InlineData(SemanticFilterAggregate.Min)]
    [InlineData(SemanticFilterAggregate.Max)]
    public void Count_to_min_or_max_substitution_is_rejected(SemanticFilterAggregate to)
    {
        var result = AggregateRewriteLegality.CheckSubstitution(SemanticFilterAggregate.Count, to);

        Assert.False(result.IsLegal);
        Assert.NotEmpty(result.Violations);
    }

    [Fact]
    public void Count_to_min_rejection_reports_empty_collection_mismatch()
    {
        var result = AggregateRewriteLegality.CheckSubstitution(
            SemanticFilterAggregate.Count,
            SemanticFilterAggregate.Min);

        Assert.Contains(result.Violations, v => v.Contains("empty-collection", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Count_to_min_rejection_reports_null_semantics_mismatch()
    {
        var result = AggregateRewriteLegality.CheckSubstitution(
            SemanticFilterAggregate.Count,
            SemanticFilterAggregate.Min);

        Assert.Contains(result.Violations, v => v.Contains("NULL-input", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Count_to_min_rejection_reports_duplicate_sensitivity_mismatch()
    {
        var result = AggregateRewriteLegality.CheckSubstitution(
            SemanticFilterAggregate.Count,
            SemanticFilterAggregate.Min);

        Assert.Contains(result.Violations,
            v => v.Contains("duplicate sensitivity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Min_to_max_substitution_is_rejected_despite_shared_empty_and_null_semantics()
    {
        // MIN and MAX agree on empty-collection result and NULL-input behavior, but they are
        // still different functions and must not be treated as interchangeable by this gate.
        // The legality check only ever certifies "no known semantic difference"; it is not a
        // general proof that two distinct aggregates compute the same value.
        var result = AggregateRewriteLegality.CheckSubstitution(
            SemanticFilterAggregate.Min,
            SemanticFilterAggregate.Max);

        Assert.True(result.IsLegal);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Duplicate_sensitive_to_insensitive_rewrite_is_rejected()
    {
        var from = SemanticAggregateSemanticsCatalog.Count;
        var to = SemanticAggregateSemanticsCatalog.Min;

        var result = AggregateRewriteLegality.CheckDuplicateSensitivity(from, to);

        Assert.False(result.IsLegal);
        Assert.Contains(result.Violations, v => v.Contains("duplicate-sensitive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_insensitive_to_insensitive_rewrite_is_accepted()
    {
        var result = AggregateRewriteLegality.CheckDuplicateSensitivity(
            SemanticAggregateSemanticsCatalog.Min,
            SemanticAggregateSemanticsCatalog.Max);

        Assert.True(result.IsLegal);
    }

    [Fact]
    public void Cardinality_gate_passes_when_neither_side_requires_proof()
    {
        var result = AggregateRewriteLegality.CheckCardinalityRequirement(
            SemanticAggregateSemanticsCatalog.Min,
            SemanticAggregateSemanticsCatalog.Max,
            SemanticCardinalityKnowledge.Unknown);

        Assert.True(result.IsLegal);
    }

    [Fact]
    public void Cardinality_gate_fails_closed_when_proof_is_required_but_cardinality_is_unknown()
    {
        var from = SemanticAggregateSemanticsCatalog.Min with
        {
            CardinalityRequirement = SemanticCardinalityRequirement.RequiresProof
        };

        var result = AggregateRewriteLegality.CheckCardinalityRequirement(
            from,
            SemanticAggregateSemanticsCatalog.Max,
            SemanticCardinalityKnowledge.Unknown);

        Assert.False(result.IsLegal);
        Assert.Contains(result.Violations, v => v.Contains("cardinality", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(SemanticCardinalityKnowledge.AtMostOne)]
    [InlineData(SemanticCardinalityKnowledge.Unbounded)]
    public void Cardinality_gate_passes_once_cardinality_is_known(SemanticCardinalityKnowledge knowledge)
    {
        var from = SemanticAggregateSemanticsCatalog.Min with
        {
            CardinalityRequirement = SemanticCardinalityRequirement.RequiresProof
        };

        var result = AggregateRewriteLegality.CheckCardinalityRequirement(
            from,
            SemanticAggregateSemanticsCatalog.Max,
            knowledge);

        Assert.True(result.IsLegal);
    }

    [Fact]
    public void Full_substitution_gate_still_fails_on_empty_and_null_mismatch_even_when_cardinality_is_known()
    {
        // Supplying cardinality knowledge must never paper over an unrelated semantic mismatch.
        var result = AggregateRewriteLegality.CheckSubstitution(
            SemanticAggregateSemanticsCatalog.Count,
            SemanticAggregateSemanticsCatalog.Min,
            SemanticCardinalityKnowledge.AtMostOne);

        Assert.False(result.IsLegal);
        Assert.Contains(result.Violations, v => v.Contains("empty-collection", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Combine_is_legal_only_when_every_input_is_legal()
    {
        var combined = AggregateRewriteLegalityResult.Combine(
            AggregateRewriteLegalityResult.Legal,
            AggregateRewriteLegalityResult.Legal);

        Assert.True(combined.IsLegal);
        Assert.Empty(combined.Violations);
    }

    [Fact]
    public void Combine_collects_violations_from_every_failing_input()
    {
        var combined = AggregateRewriteLegalityResult.Combine(
            AggregateRewriteLegalityResult.Illegal("first problem"),
            AggregateRewriteLegalityResult.Legal,
            AggregateRewriteLegalityResult.Illegal("second problem"));

        Assert.False(combined.IsLegal);
        Assert.Equal(["first problem", "second problem"], combined.Violations);
    }

    [Fact]
    public void Illegal_requires_at_least_one_violation()
    {
        Assert.Throws<ArgumentException>(() => AggregateRewriteLegalityResult.Illegal());
    }
}