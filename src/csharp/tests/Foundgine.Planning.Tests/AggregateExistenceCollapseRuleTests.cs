using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Aggregates;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class AggregateExistenceCollapseRuleTests
{
    [Theory]
    [InlineData(SemanticAggregateFilterOperator.Gt, 0, SemanticRelationshipQuantifier.Some)]
    [InlineData(SemanticAggregateFilterOperator.Gte, 1, SemanticRelationshipQuantifier.Some)]
    [InlineData(SemanticAggregateFilterOperator.Neq, 0, SemanticRelationshipQuantifier.Some)]
    [InlineData(SemanticAggregateFilterOperator.Eq, 0, SemanticRelationshipQuantifier.None)]
    [InlineData(SemanticAggregateFilterOperator.Lt, 1, SemanticRelationshipQuantifier.None)]
    [InlineData(SemanticAggregateFilterOperator.Lte, 0, SemanticRelationshipQuantifier.None)]
    public void BarePredicateBearingCount_CollapsesToMatchingQuantifier(
        SemanticAggregateFilterOperator op,
        long value,
        SemanticRelationshipQuantifier expected)
    {
        var relationship = new RelationshipId(10);
        var predicate = new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "open");
        var plan = CreatePlan(new SemanticAggregateFilter(
            relationship,
            SemanticFilterAggregate.Count,
            null,
            op,
            value,
            predicate));

        var rule = new AggregateExistenceCollapseRule(AggregateProviderCapabilityRegistry.GenericSql);
        var optimized = rule.Apply(plan);

        var result = Assert.IsType<SemanticRelationshipFilter>(optimized.Root.QueryOptions!.Filter);
        Assert.Equal(relationship, result.Relationship);
        Assert.Equal(expected, result.Quantifier);
        Assert.Equal(predicate, result.Predicate);
        Assert.Equal(
            SemanticEquivalenceFingerprint.Create(plan),
            SemanticEquivalenceFingerprint.Create(optimized));
    }

    [Theory]
    [InlineData(SemanticAggregateFilterOperator.Gte, 0)]
    [InlineData(SemanticAggregateFilterOperator.Gt, 1)]
    [InlineData(SemanticAggregateFilterOperator.Lt, 0)]
    [InlineData(SemanticAggregateFilterOperator.Lte, -1)]
    public void NonExistenceCountComparison_IsNotCollapsed(
        SemanticAggregateFilterOperator op,
        long value)
    {
        var plan = CreatePlan(new SemanticAggregateFilter(
            new RelationshipId(10),
            SemanticFilterAggregate.Count,
            null,
            op,
            value,
            new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "open")));

        var optimized = new AggregateExistenceCollapseRule(
            AggregateProviderCapabilityRegistry.GenericSql).Apply(plan);

        Assert.Same(plan.Root.QueryOptions!.Filter, optimized.Root.QueryOptions!.Filter);
    }

    [Fact]
    public void ProviderWithoutRelationshipQuantifiers_IsRejected()
    {
        var capability = new AggregateProviderCapability(
            "limited",
            [SemanticFilterAggregate.Count],
            SupportsAggregatePredicate: true,
            SupportsRelationshipQuantifiers: false);
        var plan = CreatePlan(new SemanticAggregateFilter(
            new RelationshipId(10),
            SemanticFilterAggregate.Count,
            null,
            SemanticAggregateFilterOperator.Gt,
            0,
            new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "open")));

        var rule = new AggregateExistenceCollapseRule(capability);

        Assert.False(rule.CanApply(plan));
        Assert.Same(plan, rule.Apply(plan));
    }

    [Fact]
    public void ExistingSecurityContract_IsPreserved()
    {
        var plan = new SemanticPlan(
            new SemanticPlanNode(
                1,
                ExecutionOperation.Scan,
                new EntityId(1),
                [],
                null,
                null,
                [],
                new SemanticQueryOptions(Filter: new SemanticAggregateFilter(
                    new RelationshipId(10),
                    SemanticFilterAggregate.Count,
                    null,
                    SemanticAggregateFilterOperator.Gt,
                    0,
                    new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "open")))),
            ["tenant-isolation", "authorization.required"]);

        var optimized = new AggregateExistenceCollapseRule(
            AggregateProviderCapabilityRegistry.GenericSql).Apply(plan);

        Assert.Equal(plan.EffectiveSecurityInvariants.OrderBy(x => x),
            optimized.EffectiveSecurityInvariants.OrderBy(x => x));
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