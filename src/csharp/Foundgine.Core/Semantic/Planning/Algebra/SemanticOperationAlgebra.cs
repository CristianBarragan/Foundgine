using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.IR.Graph;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning.Algebra;

/// <summary>
/// Deterministic, provider-neutral algebra over semantic operation graphs.
/// Every operation returns a new graph; the input graph is never mutated.
/// </summary>
public static class SemanticOperationAlgebra
{
    /// <summary>Validates graph topology and semantic node invariants.</summary>
    public static void Validate(SemanticOperationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var visited = new HashSet<int>();
        Visit(graph, graph.RootId, visited, parentId: null);

        if (visited.Count != graph.Nodes.Count)
            throw new InvalidOperationException("Semantic operation graph contains unreachable nodes.");
    }

    /// <summary>
    /// Conjoins an additional root predicate with the existing root predicate.
    /// This is semantic composition, not provider-specific predicate rewriting.
    /// </summary>
    public static SemanticOperationGraph Where(
        SemanticOperationGraph graph,
        SemanticFilterExpression predicate)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(predicate);
        Validate(graph);

        var operation = graph.ToOperation();
        var existing = operation.Root.QueryOptions?.Filter;
        var combined = existing is null
            ? predicate
            : new SemanticAndFilter([existing, predicate]);

        var options = operation.Root.QueryOptions ?? new SemanticQueryOptions();
        var root = operation.Root with { QueryOptions = options with { Filter = combined } };
        return SemanticOperationGraph.Create(new SemanticOperation(root));
    }

    /// <summary>
    /// Returns a canonical graph with duplicate selected/required fields removed
    /// while preserving their first occurrence. This operation is safe because
    /// field identity, rather than provider column identity, defines projection.
    /// </summary>
    public static SemanticOperationGraph Normalize(SemanticOperationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Validate(graph);

        var operation = graph.ToOperation();
        var root = NormalizeNode(operation.Root);
        return SemanticOperationGraph.Create(new SemanticOperation(root));
    }

    private static SemanticReadNode NormalizeNode(SemanticReadNode node)
    {
        var children = node.Children.Select(NormalizeNode).ToArray();
        return node with
        {
            Fields = node.Fields.Distinct().ToArray(),
            RequiredFields = node.RequiredFields.Distinct().ToArray(),
            Children = children
        };
    }

    private static void Visit(
        SemanticOperationGraph graph,
        int nodeId,
        ISet<int> visited,
        int? parentId)
    {
        if (!visited.Add(nodeId))
            throw new InvalidOperationException(
                $"Semantic operation graph contains a cycle or duplicate reference at node '{nodeId}'.");

        var node = graph.GetNode(nodeId);
        if (node.ParentId != parentId)
            throw new InvalidOperationException(
                $"Semantic operation graph parent mismatch for node '{nodeId}'.");

        if (node.IsRoot && (node.ViaRelationship is not null || node.ViaConnection is not null))
            throw new InvalidOperationException("The graph root cannot specify a parent edge.");

        if (!node.IsRoot && node.ViaRelationship is null && node.ViaConnection is null)
            throw new InvalidOperationException(
                $"Non-root node '{nodeId}' must specify a relationship or connection edge.");

        if (node.ViaRelationship is not null && node.ViaConnection is not null)
            throw new InvalidOperationException(
                $"Node '{nodeId}' cannot specify both a relationship and connection edge.");

        foreach (var childId in node.Children)
            Visit(graph, childId, visited, nodeId);
    }
}

/// <summary>
/// Canonical composition helpers for semantic query predicates.
/// </summary>
public static class SemanticPredicateAlgebra
{
    public static SemanticFilterExpression And(params SemanticFilterExpression[] predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var terms = predicates.Where(x => x is not null).ToArray();
        if (terms.Length == 0)
            throw new ArgumentException("At least one predicate is required.", nameof(predicates));
        if (terms.Length == 1)
            return terms[0];
        return new SemanticAndFilter(terms);
    }

    public static SemanticFilterExpression Or(params SemanticFilterExpression[] predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var terms = predicates.Where(x => x is not null).ToArray();
        if (terms.Length == 0)
            throw new ArgumentException("At least one predicate is required.", nameof(predicates));
        if (terms.Length == 1)
            return terms[0];
        return new SemanticOrFilter(terms);
    }
}