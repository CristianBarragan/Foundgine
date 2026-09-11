using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.E2E.Tests;

public sealed class AggregateCardinalityOptimizationRuleTests
{
    [Fact]
    public void CountGreaterThanZero_UsesExistsShortCircuit()
    {
        var plan = CreatePlan(new SemanticAggregateFilter(
            new RelationshipId(10), SemanticFilterAggregate.Count, null,
            SemanticAggregateFilterOperator.Gt, 0));

        var rule = new AggregateCardinalityOptimizationRule();
        var optimized = rule.Apply(plan);

        Assert.Equal(AggregateExecutionStrategy.CountExistsShortCircuit,
            optimized.Root.AggregateExecutionStrategy);
        Assert.Equal(SemanticEquivalenceFingerprint.Create(plan),
            SemanticEquivalenceFingerprint.Create(optimized));
    }

    [Fact]
    public void CountEqualZero_UsesEmptyShortCircuit()
    {
        var plan = CreatePlan(new SemanticAggregateFilter(
            new RelationshipId(10), SemanticFilterAggregate.Count, null,
            SemanticAggregateFilterOperator.Eq, 0));

        var optimized = new AggregateCardinalityOptimizationRule().Apply(plan);

        Assert.Equal(AggregateExecutionStrategy.CountEmptyShortCircuit,
            optimized.Root.AggregateExecutionStrategy);
    }


    [Theory]
    [InlineData(SemanticAggregateFilterOperator.Gt, 0, AggregateExecutionStrategy.CountExistsShortCircuit)]
    [InlineData(SemanticAggregateFilterOperator.Gte, 1, AggregateExecutionStrategy.CountExistsShortCircuit)]
    [InlineData(SemanticAggregateFilterOperator.Neq, 0, AggregateExecutionStrategy.CountExistsShortCircuit)]
    [InlineData(SemanticAggregateFilterOperator.Eq, 0, AggregateExecutionStrategy.CountEmptyShortCircuit)]
    [InlineData(SemanticAggregateFilterOperator.Lt, 1, AggregateExecutionStrategy.CountEmptyShortCircuit)]
    [InlineData(SemanticAggregateFilterOperator.Lte, 0, AggregateExecutionStrategy.CountEmptyShortCircuit)]
    public void EveryExistenceEquivalentBoundary_UsesTheResolverDefinition(
        SemanticAggregateFilterOperator op, long value, AggregateExecutionStrategy expected)
    {
        var plan = CreatePlan(new SemanticAggregateFilter(
            new RelationshipId(10), SemanticFilterAggregate.Count, null, op, value));

        var optimized = new AggregateCardinalityOptimizationRule().Apply(plan);

        Assert.Equal(expected, optimized.Root.AggregateExecutionStrategy);
    }

    [Fact]
    public void OptimizationPreservesSecurityInvariants()
    {
        var plan = new SemanticPlan(
            CreatePlan(new SemanticAggregateFilter(
                new RelationshipId(10), SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Gt, 0)).Root,
            [
                SecurityInvariantIds.AuthorizationRequired,
                SecurityInvariantIds.RuntimeAuthorization,
                SecurityInvariantIds.TenantIsolation,
                SecurityInvariantIds.RelationshipVisibility,
                SecurityInvariantIds.ParameterizedValues,
                SecurityInvariantIds.PlanCacheContextIsolation
            ]);

        var optimized = new AggregateCardinalityOptimizationRule().Apply(plan);

        Assert.Equal(plan.EffectiveSecurityInvariants.OrderBy(x => x),
            optimized.EffectiveSecurityInvariants.OrderBy(x => x));
        Assert.Equal(SemanticEquivalenceFingerprint.Create(plan),
            SemanticEquivalenceFingerprint.Create(optimized));
    }

    [Fact]
    public void CountGreaterThanOne_IsNotReducedToExists()
    {
        var plan = CreatePlan(new SemanticAggregateFilter(
            new RelationshipId(10), SemanticFilterAggregate.Count, null,
            SemanticAggregateFilterOperator.Gt, 1));

        var optimized = new AggregateCardinalityOptimizationRule().Apply(plan);

        Assert.Equal(AggregateExecutionStrategy.Default,
            optimized.Root.AggregateExecutionStrategy);
    }

    [Fact]
    public void CountWithPredicate_IsNotReducedToExistenceStrategy()
    {
        var plan = CreatePlan(new SemanticAggregateFilter(
            new RelationshipId(10), SemanticFilterAggregate.Count, null,
            SemanticAggregateFilterOperator.Gt, 0,
            Predicate: new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "active")));

        var optimized = new AggregateCardinalityOptimizationRule().Apply(plan);

        Assert.Equal(AggregateExecutionStrategy.Default, optimized.Root.AggregateExecutionStrategy);
        Assert.Equal(SemanticEquivalenceFingerprint.Create(plan),
            SemanticEquivalenceFingerprint.Create(optimized));
    }

    [Fact]
    public void CountWithTargetField_IsNotReducedToExistenceStrategy()
    {
        var plan = CreatePlan(new SemanticAggregateFilter(
            new RelationshipId(10), SemanticFilterAggregate.Count, new FieldId(9),
            SemanticAggregateFilterOperator.Gt, 0));

        var optimized = new AggregateCardinalityOptimizationRule().Apply(plan);

        Assert.Equal(AggregateExecutionStrategy.Default, optimized.Root.AggregateExecutionStrategy);
    }

    [Fact]
    public void NegativeBoundary_IsNotReducedToExistenceStrategy()
    {
        var plan = CreatePlan(new SemanticAggregateFilter(
            new RelationshipId(10), SemanticFilterAggregate.Count, null,
            SemanticAggregateFilterOperator.Gte, 0));

        var optimized = new AggregateCardinalityOptimizationRule().Apply(plan);

        Assert.Equal(AggregateExecutionStrategy.Default, optimized.Root.AggregateExecutionStrategy);
    }

    [Fact]
    public void MixedAggregateStrategies_AreLeftUntouched()
    {
        var filter = new SemanticAndFilter([
            new SemanticAggregateFilter(new RelationshipId(10), SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Gt, 0),
            new SemanticAggregateFilter(new RelationshipId(11), SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Eq, 0)
        ]);

        var optimized = new AggregateCardinalityOptimizationRule().Apply(CreatePlan(filter));

        Assert.Equal(AggregateExecutionStrategy.Default, optimized.Root.AggregateExecutionStrategy);
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