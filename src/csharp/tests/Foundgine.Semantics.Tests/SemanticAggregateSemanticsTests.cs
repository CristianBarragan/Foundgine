using Foundgine.Core.Semantic.Aggregates;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticAggregateSemanticsTests
{
    [Fact]
    public void Count_returns_zero_for_empty_collection()
    {
        var semantics = SemanticAggregateSemanticsCatalog.For(SemanticFilterAggregate.Count);

        Assert.Equal(SemanticEmptyCollectionResult.Zero, semantics.EmptyCollectionResult);
    }

    [Fact]
    public void Count_is_never_null()
    {
        var semantics = SemanticAggregateSemanticsCatalog.For(SemanticFilterAggregate.Count);

        Assert.Equal(SemanticNullInputBehavior.NeverNull, semantics.NullInputBehavior);
    }

    [Fact]
    public void Count_is_duplicate_sensitive()
    {
        var semantics = SemanticAggregateSemanticsCatalog.For(SemanticFilterAggregate.Count);

        Assert.True(semantics.IsDuplicateSensitive);
    }

    [Fact]
    public void Min_returns_null_for_empty_collection()
    {
        var semantics = SemanticAggregateSemanticsCatalog.For(SemanticFilterAggregate.Min);

        Assert.Equal(SemanticEmptyCollectionResult.Null, semantics.EmptyCollectionResult);
    }

    [Fact]
    public void Max_returns_null_for_empty_collection()
    {
        var semantics = SemanticAggregateSemanticsCatalog.For(SemanticFilterAggregate.Max);

        Assert.Equal(SemanticEmptyCollectionResult.Null, semantics.EmptyCollectionResult);
    }

    [Theory]
    [InlineData(SemanticFilterAggregate.Min)]
    [InlineData(SemanticFilterAggregate.Max)]
    public void Min_and_max_ignore_null_input(SemanticFilterAggregate aggregate)
    {
        var semantics = SemanticAggregateSemanticsCatalog.For(aggregate);

        Assert.Equal(SemanticNullInputBehavior.IgnoresNull, semantics.NullInputBehavior);
    }

    [Theory]
    [InlineData(SemanticFilterAggregate.Min)]
    [InlineData(SemanticFilterAggregate.Max)]
    public void Min_and_max_are_duplicate_insensitive(SemanticFilterAggregate aggregate)
    {
        var semantics = SemanticAggregateSemanticsCatalog.For(aggregate);

        Assert.False(semantics.IsDuplicateSensitive);
    }

    [Fact]
    public void Catalog_exposes_every_registered_aggregate()
    {
        var aggregates = SemanticAggregateSemanticsCatalog.All.Select(x => x.Aggregate).ToArray();

        Assert.Contains(SemanticFilterAggregate.Count, aggregates);
        Assert.Contains(SemanticFilterAggregate.Min, aggregates);
        Assert.Contains(SemanticFilterAggregate.Max, aggregates);
        Assert.Equal(3, aggregates.Length);
    }

    [Fact]
    public void For_throws_for_unregistered_aggregate()
    {
        Assert.Throws<NotSupportedException>(() =>
            SemanticAggregateSemanticsCatalog.For((SemanticFilterAggregate)byte.MaxValue));
    }

    [Fact]
    public void TryGet_returns_false_for_unregistered_aggregate()
    {
        var found = SemanticAggregateSemanticsCatalog.TryGet((SemanticFilterAggregate)byte.MaxValue, out var semantics);

        Assert.False(found);
        Assert.Null(semantics);
    }

    [Fact]
    public void TryGet_returns_true_and_matches_For_for_registered_aggregate()
    {
        var found = SemanticAggregateSemanticsCatalog.TryGet(SemanticFilterAggregate.Count, out var semantics);

        Assert.True(found);
        Assert.Equal(SemanticAggregateSemanticsCatalog.For(SemanticFilterAggregate.Count), semantics);
    }

    [Fact]
    public void Default_aggregates_do_not_require_a_cardinality_proof()
    {
        Assert.All(
            SemanticAggregateSemanticsCatalog.All,
            semantics => Assert.False(semantics.RequiresCardinalityProof));
    }
}
