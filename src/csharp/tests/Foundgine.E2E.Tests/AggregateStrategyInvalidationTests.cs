using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.E2E.Tests;

public sealed class AggregateStrategyInvalidationTests
{
    [Fact]
    public void CardinalityRule_ClearsStaleHint_WhenFilterIsNoLongerEligible()
    {
        var relationship = new RelationshipId(1);
        var filter = new SemanticAggregateFilter(
            relationship,
            SemanticFilterAggregate.Count,
            null,
            SemanticAggregateFilterOperator.Gt,
            0,
            new SemanticFieldFilter(
                new FieldId(2),
                SemanticFilterOperator.Eq,
                true));

        var plan = CreatePlan(filter, AggregateExecutionStrategy.CountExistsShortCircuit);

        var result = new AggregateCardinalityOptimizationRule().Apply(plan);

        Assert.Equal(AggregateExecutionStrategy.Default, result.Root.AggregateExecutionStrategy);
    }

    [Fact]
    public void PushdownRule_ClearsPreexistingCardinalityHint()
    {
        var relationship = new RelationshipId(1);
        var predicate = new SemanticFieldFilter(
            new FieldId(2),
            SemanticFilterOperator.Eq,
            true);
        var filter = new SemanticAndFilter([
            new SemanticAggregateFilter(relationship, SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Gt, 0, null),
            new SemanticRelationshipFilter(relationship, SemanticRelationshipQuantifier.Some, predicate)
        ]);

        var plan = CreatePlan(filter, AggregateExecutionStrategy.CountExistsShortCircuit);

        var result = new AggregateRelationshipFilterPushdownRule().Apply(plan);

        Assert.Equal(AggregateExecutionStrategy.Default, result.Root.AggregateExecutionStrategy);
        var aggregate = Assert.IsType<SemanticAggregateFilter>(result.Root.QueryOptions!.Filter);
        Assert.NotNull(aggregate.Predicate);
    }


    private static SemanticPlan CreatePlan(
        SemanticFilterExpression filter,
        AggregateExecutionStrategy strategy = AggregateExecutionStrategy.Default) =>
        new(new SemanticPlanNode(
            1,
            ExecutionOperation.Scan,
            new EntityId(1),
            [],
            null,
            null,
            [],
            new SemanticQueryOptions(Filter: filter),
            AggregateExecutionStrategy: strategy));
}