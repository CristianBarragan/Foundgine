using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.IR;

/// <summary>
/// Lowers the resolved SemanticGraph into canonical Semantic IR.
/// This operation is deliberately lossless for information owned by the
/// semantic layer. No provider or storage information is introduced.
/// </summary>
public static class SemanticOperationCompiler
{
    public static SemanticOperation Compile(SemanticGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.Nodes.Count == 0)
            throw new InvalidOperationException("Cannot compile an empty semantic graph.");

        var byParent = graph.Nodes.ToLookup(node => node.ParentId);
        var roots = byParent[null].ToArray();

        if (roots.Length != 1)
            throw new InvalidOperationException(
                "A semantic operation requires exactly one root semantic node.");

        var nodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
        var visited = new HashSet<int>();

        var root = Build(
            roots[0],
            byParent,
            nodeIds,
            visited,
            graph.Options,
            isRoot: true);

        if (visited.Count != graph.Nodes.Count)
        {
            var unreachable = graph.Nodes
                .Where(node => !visited.Contains(node.Id))
                .Select(node => node.Id);

            throw new InvalidOperationException(
                $"Semantic graph contains unreachable nodes: {string.Join(", ", unreachable)}.");
        }

        return new SemanticOperation(root);
    }

    private static SemanticReadNode Build(
        SemanticGraphNode node,
        ILookup<int?, SemanticGraphNode> byParent,
        IReadOnlySet<int> nodeIds,
        ISet<int> visited,
        SemanticQueryOptions? queryOptions,
        bool isRoot)
    {
        if (!visited.Add(node.Id))
            throw new InvalidOperationException(
                $"Semantic graph contains a cycle at node {node.Id}.");

        if (node.ParentId is { } parentId && !nodeIds.Contains(parentId))
            throw new InvalidOperationException(
                $"Semantic node {node.Id} references missing parent node {parentId}.");

        if (!isRoot && node.ViaRelationship is null && node.ViaConnection is null)
            throw new InvalidOperationException(
                $"Non-root semantic node {node.Id} must specify a relationship or connection.");

        if (isRoot && (node.ViaRelationship is not null || node.ViaConnection is not null))
            throw new InvalidOperationException(
                $"Root semantic node {node.Id} cannot specify a parent edge.");

        var children = byParent[node.Id]
            .Select(child => Build(
                child,
                byParent,
                nodeIds,
                visited,
                null,
                isRoot: false))
            .ToArray();

        return new SemanticReadNode(
            node.Id,
            node.EntityId,
            node.Fields.ToArray(),
            node.ViaRelationship,
            node.ViaConnection,
            children,
            isRoot ? queryOptions : null,
            node.Authorization);
    }
}