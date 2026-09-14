using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class AggregateRelationshipFilterPushdownRuleTests
{
    [Fact]
    public void CountExistsAndSomePredicate_AreMergedIntoFilteredCount()
    {
        var relationship = new RelationshipId(10);
        var predicate = new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "open");
        var filter = new SemanticAndFilter([
            new SemanticAggregateFilter(
                relationship,
                SemanticFilterAggregate.Count,
                null,
                SemanticAggregateFilterOperator.Gt,
                0),
            new SemanticRelationshipFilter(
                relationship,
                SemanticRelationshipQuantifier.Some,
                predicate)
        ]);

        var plan = CreatePlan(filter);
        var optimized = new AggregateRelationshipFilterPushdownRule().Apply(plan);

        var aggregate = Assert.IsType<SemanticAggregateFilter>(optimized.Root.QueryOptions!.Filter);
        Assert.Equal(relationship, aggregate.Relationship);
        Assert.Equal(SemanticFilterAggregate.Count, aggregate.Aggregate);
        Assert.Equal(SemanticAggregateFilterOperator.Gt, aggregate.Operator);
        Assert.Equal(0, aggregate.Value);
        Assert.Equal(predicate, aggregate.Predicate);
        Assert.Equal(SemanticEquivalenceFingerprint.Create(plan),
            SemanticEquivalenceFingerprint.Create(optimized));
    }


    [Fact]
    public void Pushdown_preserves_semantic_fingerprint()
    {
        var relationship = new RelationshipId(10);
        var predicate = new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "open");
        var original = CreatePlan(new SemanticAndFilter([
            new SemanticAggregateFilter(relationship, SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Gt, 0),
            new SemanticRelationshipFilter(relationship, SemanticRelationshipQuantifier.Some, predicate)
        ]));

        var optimized = new AggregateRelationshipFilterPushdownRule().Apply(original);

        Assert.Equal(
            SemanticEquivalenceFingerprint.Create(original),
            SemanticEquivalenceFingerprint.Create(optimized));
    }

    [Fact]
    public void Optimizer_accepts_the_pushdown_as_semantically_equivalent()
    {
        var relationship = new RelationshipId(10);
        var predicate = new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "open");
        var plan = CreatePlan(new SemanticAndFilter([
            new SemanticAggregateFilter(relationship, SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Gt, 0),
            new SemanticRelationshipFilter(relationship, SemanticRelationshipQuantifier.Some, predicate)
        ]));

        var result = new SemanticPlanOptimizer([
            new AggregateRelationshipFilterPushdownRule(), new AggregateCardinalityOptimizationRule()
        ]).Optimize(plan);

        Assert.True(result.SemanticProof.IsSatisfied);
        var aggregate = Assert.IsType<SemanticAggregateFilter>(result.Plan.Root.QueryOptions!.Filter);
        Assert.Equal(predicate, aggregate.Predicate);
    }

    [Fact]
    public void FullOptimizer_PushdownRunsBeforeCardinalityAndDoesNotLeaveStaleHint()
    {
        var relationship = new RelationshipId(10);
        var predicate = new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "open");
        var plan = CreatePlan(new SemanticAndFilter([
            new SemanticAggregateFilter(relationship, SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Gt, 0),
            new SemanticRelationshipFilter(relationship, SemanticRelationshipQuantifier.Some, predicate)
        ]));

        var result = new SemanticPlanOptimizer().Optimize(plan);

        Assert.True(result.SemanticProof.IsSatisfied);
        Assert.True(result.SecurityProof.IsSatisfied);
        Assert.Contains("aggregate.relationship-filter.pushdown", result.AppliedRules);
        Assert.DoesNotContain("aggregate.cardinality.short-circuit", result.AppliedRules);
        Assert.Equal(AggregateExecutionStrategy.Default, result.Plan.Root.AggregateExecutionStrategy);

        var aggregate = Assert.IsType<SemanticAggregateFilter>(result.Plan.Root.QueryOptions!.Filter);
        Assert.Equal(predicate, aggregate.Predicate);
    }


    [Fact]
    public void CountGreaterThanOne_IsNotEligible()
    {
        var relationship = new RelationshipId(10);
        var filter = new SemanticAndFilter([
            new SemanticAggregateFilter(relationship, SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Gt, 1),
            new SemanticRelationshipFilter(relationship, SemanticRelationshipQuantifier.Some,
                new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "open"))
        ]);

        var optimized = new AggregateRelationshipFilterPushdownRule().Apply(CreatePlan(filter));

        Assert.Same(filter, optimized.Root.QueryOptions!.Filter);
    }


    [Theory]
    [InlineData(SemanticAggregateFilterOperator.Gte, 0)]
    [InlineData(SemanticAggregateFilterOperator.Gte, -1)]
    [InlineData(SemanticAggregateFilterOperator.Lt, 0)]
    [InlineData(SemanticAggregateFilterOperator.Lt, -1)]
    public void NonExistenceEquivalentCountComparisons_AreNotEligibleForExistsPushdown(
        SemanticAggregateFilterOperator op, long value)
    {
        var relationship = new RelationshipId(10);
        var filter = new SemanticAndFilter([
            new SemanticAggregateFilter(relationship, SemanticFilterAggregate.Count, null, op, value),
            new SemanticRelationshipFilter(relationship, SemanticRelationshipQuantifier.Some,
                new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "open"))
        ]);

        var optimized = new AggregateRelationshipFilterPushdownRule().Apply(CreatePlan(filter));

        Assert.Same(filter, optimized.Root.QueryOptions!.Filter);
    }

    [Fact]
    public void DifferentRelationships_AreNotMerged()
    {
        var filter = new SemanticAndFilter([
            new SemanticAggregateFilter(new RelationshipId(10), SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Gt, 0),
            new SemanticRelationshipFilter(new RelationshipId(11), SemanticRelationshipQuantifier.Some,
                new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "open"))
        ]);

        var optimized = new AggregateRelationshipFilterPushdownRule().Apply(CreatePlan(filter));

        Assert.Same(filter, optimized.Root.QueryOptions!.Filter);
    }

    [Fact]
    public void AggregateWithExistingPredicate_IsNotRewritten()
    {
        var relationship = new RelationshipId(10);
        var existing = new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "open");
        var filter = new SemanticAndFilter([
            new SemanticAggregateFilter(relationship, SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Gt, 0, existing),
            new SemanticRelationshipFilter(relationship, SemanticRelationshipQuantifier.Some,
                new SemanticFieldFilter(new FieldId(4), SemanticFilterOperator.Eq, "active"))
        ]);

        var optimized = new AggregateRelationshipFilterPushdownRule().Apply(CreatePlan(filter));

        Assert.Same(filter, optimized.Root.QueryOptions!.Filter);
    }

    private static SemanticPlan CreatePlan(SemanticFilterExpression filter) =>
        new(new SemanticPlanNode(
            1,
            ExecutionOperation.Scan,
            new EntityId(1),
            [],
            null,
            null,
            [],
            new SemanticQueryOptions(Filter: filter)));
}