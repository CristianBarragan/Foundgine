using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning.Algebra;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.IR.Graph;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class SemanticOperationAlgebraStep30Tests
{
    [Fact]
    public void Where_ComposesWithoutMutatingOriginalGraph()
    {
        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(1), [new FieldId(11)]);
        var original = SemanticOperationGraph.Create(SemanticOperationCompiler.Compile(graph));
        var predicate = new SemanticFieldFilter(new FieldId(11), SemanticFilterOperator.Eq, 42);

        var composed = SemanticOperationAlgebra.Where(original, predicate);

        Assert.Null(original.Root.QueryOptions);
        Assert.NotNull(composed.Root.QueryOptions);
        Assert.Same(predicate, composed.Root.QueryOptions!.Filter);
    }

    [Fact]
    public void Normalize_RemovesDuplicateFieldsDeterministically()
    {
        var operation = new SemanticOperation(new SemanticReadNode(
            0,
            new EntityId(1),
            [new FieldId(11), new FieldId(11), new FieldId(12)],
            null,
            null,
            []));

        var graph = SemanticOperationGraph.Create(operation);
        var normalized = SemanticOperationAlgebra.Normalize(graph);

        Assert.Equal([new FieldId(11), new FieldId(12)], normalized.Root.Fields);
    }

    [Fact]
    public void GraphFingerprint_IsStableAcrossEquivalentSnapshots()
    {
        var first = new SemanticGraph();
        first.AddRoot(new EntityId(1), [new FieldId(11), new FieldId(12)]);
        var second = new SemanticGraph();
        second.AddRoot(new EntityId(1), [new FieldId(11), new FieldId(12)]);

        var left = SemanticOperationGraph.Create(SemanticOperationCompiler.Compile(first));
        var right = SemanticOperationGraph.Create(SemanticOperationCompiler.Compile(second));

        Assert.Equal(
            SemanticOperationGraphFingerprint.Create(left),
            SemanticOperationGraphFingerprint.Create(right));
    }

    [Fact]
    public void PredicateAlgebra_SingleTermDoesNotCreateUnnecessaryBooleanNode()
    {
        var predicate = new SemanticFieldFilter(new FieldId(11), SemanticFilterOperator.Eq, 42);

        Assert.Same(predicate, SemanticPredicateAlgebra.And(predicate));
        Assert.Same(predicate, SemanticPredicateAlgebra.Or(predicate));
    }
}