using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.IR.Graph;
using Foundgine.Core.Semantic.Security.Execution;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticOperationGraphSafetyStep32Tests
{
    [Fact]
    public void RejectsExcessiveDepth()
    {
        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(1UL), [new FieldId(11UL)]);
        var current = root;
        for (var i = 0; i < 4; i++)
            current = graph.Add(new EntityId((ulong)(2 + i)), new RelationshipId((ulong)(20 + i)), current,
                [new FieldId((ulong)(30 + i))]);

        var operationGraph = SemanticOperationGraph.Create(SemanticOperationCompiler.Compile(graph));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SemanticOperationGraphSafetyValidator.Validate(operationGraph,
                new SecurityResourceLimits { MaxOperationGraphDepth = 3 }));

        Assert.Contains("depth", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsExcessiveNodes()
    {
        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(1UL), [new FieldId(11UL)]);
        graph.Add(new EntityId(2UL), new RelationshipId(12UL), root, [new FieldId(21UL)]);
        graph.Add(new EntityId(3UL), new RelationshipId(13UL), root, [new FieldId(31UL)]);

        var operationGraph = SemanticOperationGraph.Create(SemanticOperationCompiler.Compile(graph));

        Assert.Throws<InvalidOperationException>(() =>
            SemanticOperationGraphSafetyValidator.Validate(operationGraph,
                new SecurityResourceLimits { MaxOperationGraphNodes = 2 }));
    }

    [Fact]
    public void AcceptsBoundedGraph()
    {
        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(1UL), [new FieldId(11UL)]);
        graph.Add(new EntityId(2UL), new RelationshipId(12UL), root, [new FieldId(21UL)]);

        var operationGraph = SemanticOperationGraph.Create(SemanticOperationCompiler.Compile(graph));

        SemanticOperationGraphSafetyValidator.Validate(operationGraph, new SecurityResourceLimits
        {
            MaxOperationGraphNodes = 2,
            MaxOperationGraphDepth = 2,
            MaxOperationGraphEdges = 1,
            MaxOperationGraphFields = 2
        });
    }
}