using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.IR.Graph;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticOperationGraphStep30Tests
{
    [Fact]
    public void Graph_ExposesExplicitTopology_WithoutProviderConcepts()
    {
        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(1), [new FieldId(11)]);
        graph.Add(new EntityId(2), new RelationshipId(12), root, [new FieldId(21)]);

        var operation = SemanticOperationCompiler.Compile(graph);
        var operationGraph = SemanticOperationGraph.Create(operation);

        Assert.Equal(2, operationGraph.Nodes.Count);
        Assert.True(operationGraph.Root.IsRoot);
        Assert.Single(operationGraph.Root.Children);
        var child = operationGraph.GetNode(operationGraph.Root.Children[0]);
        Assert.Equal(operationGraph.Root.Id, child.ParentId);
        Assert.Equal(new RelationshipId(12), child.ViaRelationship);
        Assert.Null(child.ViaConnection);
    }

    [Fact]
    public void Graph_RoundTrips_ToCanonicalSemanticIr()
    {
        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(1), [new FieldId(11)]);
        graph.Add(new EntityId(2), new RelationshipId(12), root, [new FieldId(21)]);

        var operation = SemanticOperationCompiler.Compile(graph);
        var roundTrip = SemanticOperationGraph.Create(operation).ToOperation();

        Assert.Equal(operation.Root.Id, roundTrip.Root.Id);
        Assert.Equal(operation.Root.EntityId, roundTrip.Root.EntityId);
        Assert.Equal(operation.Root.Fields, roundTrip.Root.Fields);
        Assert.Equal(operation.Root.RequiredFields, roundTrip.Root.RequiredFields);
        Assert.Equal(operation.Root.ViaRelationship, roundTrip.Root.ViaRelationship);
        Assert.Equal(operation.Root.ViaConnection, roundTrip.Root.ViaConnection);
        Assert.Equal(operation.Root.QueryOptions, roundTrip.Root.QueryOptions);
        Assert.Equal(operation.Root.Authorization, roundTrip.Root.Authorization);
        Assert.Equal(operation.Root.Children.Count, roundTrip.Root.Children.Count);
        Assert.Equal(operation.Root.Children[0].Id, roundTrip.Root.Children[0].Id);
        Assert.Equal(operation.Root.Children[0].EntityId, roundTrip.Root.Children[0].EntityId);
        Assert.Equal(operation.Root.Children[0].Fields, roundTrip.Root.Children[0].Fields);
        Assert.Equal(operation.Root.Children[0].ViaRelationship, roundTrip.Root.Children[0].ViaRelationship);
        Assert.True(roundTrip.IsReadOnly);
    }

    [Fact]
    public void Graph_IsNotAffectedBySourceMutationAfterCreation()
    {
        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(1), [new FieldId(11)]);
        var operationGraph = SemanticOperationGraph.Create(SemanticOperationCompiler.Compile(graph));

        graph.AddRoot(new EntityId(99), [new FieldId(990)]);

        Assert.Single(operationGraph.Nodes);
        Assert.Equal([new FieldId(11)], operationGraph.Root.Fields);
    }
}