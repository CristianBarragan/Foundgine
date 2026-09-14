using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class RelationshipTraversalOptimizationRuleTests
{
    [Fact]
    public void OneRelationshipBecomesSingleHop()
    {
        var node = new SemanticPlanNode(
            2,
            ExecutionOperation.Traverse,
            new EntityId(2),
            [new FieldId(2)],
            new RelationshipId(7),
            null,
            [],
            RelationshipCardinality: RelationshipCardinality.One);
        var plan = new SemanticPlan(new SemanticPlanNode(
            1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [node]));

        var optimized = new RelationshipTraversalOptimizationRule().Apply(plan);

        Assert.Equal(RelationshipTraversalMode.SingleHop, optimized.Root.Children[0].TraversalMode);
        Assert.Equal(RelationshipCardinality.One, optimized.Root.Children[0].RelationshipCardinality);
    }

    [Fact]
    public void ManyRelationshipBecomesSetBased()
    {
        var node = new SemanticPlanNode(
            2,
            ExecutionOperation.Traverse,
            new EntityId(2),
            [new FieldId(2)],
            new RelationshipId(7),
            null,
            [],
            RelationshipCardinality: RelationshipCardinality.Many);
        var plan = new SemanticPlan(new SemanticPlanNode(
            1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [node]));

        var optimized = new RelationshipTraversalOptimizationRule().Apply(plan);

        Assert.Equal(RelationshipTraversalMode.SetBased, optimized.Root.Children[0].TraversalMode);
    }

    [Fact]
    public void MissingCardinalityDoesNotRewrite()
    {
        var node = new SemanticPlanNode(
            2,
            ExecutionOperation.Traverse,
            new EntityId(2),
            [new FieldId(2)],
            new RelationshipId(7),
            null,
            []);
        var plan = new SemanticPlan(new SemanticPlanNode(
            1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [node]));

        var optimized = new RelationshipTraversalOptimizationRule().Apply(plan);

        Assert.Same(plan, optimized);
    }

    [Fact]
    public void TraversalHintDoesNotChangeSemanticEquivalence()
    {
        var node = new SemanticPlanNode(
            2,
            ExecutionOperation.Traverse,
            new EntityId(2),
            [new FieldId(2)],
            new RelationshipId(7),
            null,
            [],
            RelationshipCardinality: RelationshipCardinality.One);
        var before = new SemanticPlan(new SemanticPlanNode(
            1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [node]));
        var after = new RelationshipTraversalOptimizationRule().Apply(before);

        var proof = SemanticEquivalenceProof.Create(before, after);

        Assert.True(proof.IsSatisfied);
    }
}
