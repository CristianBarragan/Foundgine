using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.IR;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class SemanticContractPlanningBoundaryTests
{
    [Fact]
    public void Planner_accepts_operation_belonging_to_frozen_contract()
    {
        var contract = CreateContract();
        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(1), [new FieldId(1)]);
        graph.Add(new EntityId(2), new RelationshipId(1), root, [new FieldId(2)]);
        var operation = SemanticOperationCompiler.Compile(graph);

        var plan = new Planner().Plan(contract, operation);

        Assert.Equal(new EntityId(1), plan.Root.EntityId);
        Assert.Equal(new EntityId(2), Assert.Single(plan.Root.Children).EntityId);
    }

    [Fact]
    public void Planner_rejects_operation_with_unknown_entity_identity()
    {
        var contract = CreateContract();
        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(999), [new FieldId(1)]);
        var operation = SemanticOperationCompiler.Compile(graph);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Planner().Plan(contract, operation));

        Assert.Contains("999", exception.Message);
    }

    [Fact]
    public void Planner_rejects_operation_with_unknown_field_identity()
    {
        var contract = CreateContract();
        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(1), [new FieldId(999)]);
        var operation = SemanticOperationCompiler.Compile(graph);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Planner().Plan(contract, operation));

        Assert.Contains("unknown", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Planner_rejects_relationship_target_that_disagrees_with_contract()
    {
        var contract = CreateContract();
        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(1));
        graph.Add(new EntityId(3), new RelationshipId(1), root);
        var operation = SemanticOperationCompiler.Compile(graph);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Planner().Plan(contract, operation));

        Assert.Contains("targets", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SemanticContractSnapshot CreateContract()
    {
#pragma warning disable CS0618
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", entity => entity
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(10), "Name", typeof(string))
                .Relationship(new RelationshipId(1), "Orders", new EntityId(2), RelationshipCardinality.Many))
            .Entity(new EntityId(2), "Order", entity => entity
                .Identity(new FieldId(2), "Id")
                .Field(new FieldId(20), "Number", typeof(string)))
            .Build();
#pragma warning restore CS0618

        return model.Freeze().CreateSnapshot();
    }
}