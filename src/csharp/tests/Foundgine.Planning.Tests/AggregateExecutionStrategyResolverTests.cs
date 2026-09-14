using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class AggregateExecutionStrategyResolverTests
{
    [Theory]
    [InlineData(SemanticAggregateFilterOperator.Gt, 0)]
    [InlineData(SemanticAggregateFilterOperator.Gte, 1)]
    [InlineData(SemanticAggregateFilterOperator.Neq, 0)]
    public void ComparisonsEquivalentToNonEmpty_ResolveToExistsShortCircuit(
        SemanticAggregateFilterOperator op, long value)
    {
        Assert.Equal(
            AggregateExecutionStrategy.CountExistsShortCircuit,
            AggregateExecutionStrategyResolver.Resolve(op, value));
    }

    [Theory]
    [InlineData(SemanticAggregateFilterOperator.Eq, 0)]
    [InlineData(SemanticAggregateFilterOperator.Lt, 1)]
    [InlineData(SemanticAggregateFilterOperator.Lte, 0)]
    public void ComparisonsEquivalentToEmpty_ResolveToEmptyShortCircuit(
        SemanticAggregateFilterOperator op, long value)
    {
        Assert.Equal(
            AggregateExecutionStrategy.CountEmptyShortCircuit,
            AggregateExecutionStrategyResolver.Resolve(op, value));
    }

    [Theory]
    [InlineData(SemanticAggregateFilterOperator.Gte, 0)]
    [InlineData(SemanticAggregateFilterOperator.Gte, -1)]
    [InlineData(SemanticAggregateFilterOperator.Lt, 0)]
    [InlineData(SemanticAggregateFilterOperator.Lt, -1)]
    [InlineData(SemanticAggregateFilterOperator.Gt, 1)]
    [InlineData(SemanticAggregateFilterOperator.Eq, 5)]
    [InlineData(SemanticAggregateFilterOperator.Lte, 3)]
    public void ComparisonsThatDependOnExactCount_DoNotResolve(
        SemanticAggregateFilterOperator op, long value)
    {
        Assert.Null(AggregateExecutionStrategyResolver.Resolve(op, value));
    }


    [Theory]
    [InlineData(SemanticAggregateFilterOperator.Gt, 0)]
    [InlineData(SemanticAggregateFilterOperator.Gte, 1)]
    [InlineData(SemanticAggregateFilterOperator.Neq, 0)]
    [InlineData(SemanticAggregateFilterOperator.Eq, 0)]
    [InlineData(SemanticAggregateFilterOperator.Lt, 1)]
    [InlineData(SemanticAggregateFilterOperator.Lte, 0)]
    public void StringIntegralValues_UseTheSameCanonicalResolution(
        SemanticAggregateFilterOperator op, long value)
    {
        Assert.Equal(
            AggregateExecutionStrategyResolver.Resolve(op, value),
            AggregateExecutionStrategyResolver.Resolve(op, value.ToString()));
    }

    [Fact]
    public void NonIntegralValue_DoesNotResolve()
    {
        Assert.Null(AggregateExecutionStrategyResolver.Resolve(SemanticAggregateFilterOperator.Gt, "not-a-number"));
        Assert.Null(AggregateExecutionStrategyResolver.Resolve(SemanticAggregateFilterOperator.Gt, null));
    }

    [Fact]
    public void EligibleBareCountFilter_MatchingNodeStrategy_IsEligible()
    {
        var filter = new SemanticAggregateFilter(
            new RelationshipId(1), SemanticFilterAggregate.Count, null,
            SemanticAggregateFilterOperator.Gt, 0);

        Assert.True(AggregateExecutionStrategyResolver.IsEligibleFor(
            filter, AggregateExecutionStrategy.CountExistsShortCircuit));
    }

    [Fact]
    public void FilterWithTargetField_IsNeverEligible_EvenUnderNonDefaultStrategy()
    {
        var filter = new SemanticAggregateFilter(
            new RelationshipId(1), SemanticFilterAggregate.Count, new FieldId(9),
            SemanticAggregateFilterOperator.Gt, 0);

        Assert.False(AggregateExecutionStrategyResolver.IsEligibleFor(
            filter, AggregateExecutionStrategy.CountExistsShortCircuit));
    }

    [Fact]
    public void FilterWithNestedPredicate_IsNeverEligible_EvenUnderNonDefaultStrategy()
    {
        var filter = new SemanticAggregateFilter(
            new RelationshipId(1), SemanticFilterAggregate.Count, null,
            SemanticAggregateFilterOperator.Gt, 0,
            Predicate: new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.Eq, "x"));

        Assert.False(AggregateExecutionStrategyResolver.IsEligibleFor(
            filter, AggregateExecutionStrategy.CountExistsShortCircuit));
    }

    [Fact]
    public void EligibleFilter_UnderDefaultNodeStrategy_IsNotEligible()
    {
        var filter = new SemanticAggregateFilter(
            new RelationshipId(1), SemanticFilterAggregate.Count, null,
            SemanticAggregateFilterOperator.Gt, 0);

        Assert.False(AggregateExecutionStrategyResolver.IsEligibleFor(
            filter, AggregateExecutionStrategy.Default));
    }

    [Fact]
    public void FilterWhoseOwnComparisonDisagreesWithNodeStrategy_IsNotEligible()
    {
        // Defensive: a node could in principle carry a strategy that this particular filter's
        // own comparison does not itself resolve to. Eligibility is always re-derived from the
        // filter, never assumed from the node alone.
        var filter = new SemanticAggregateFilter(
            new RelationshipId(1), SemanticFilterAggregate.Count, null,
            SemanticAggregateFilterOperator.Eq, 0); // resolves to EmptyShortCircuit

        Assert.False(AggregateExecutionStrategyResolver.IsEligibleFor(
            filter, AggregateExecutionStrategy.CountExistsShortCircuit));
    }
}