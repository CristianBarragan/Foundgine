using Foundgine.Core.Semantic.Security.Execution;

namespace Foundgine.Core.Semantic.IR.Graph;

/// <summary>
/// Enforces bounded graph shape after dynamic intent has been resolved into
/// canonical semantic operations. This is intentionally provider-neutral and
/// protects callers that construct operation graphs without going through the
/// JSON/MCP adapters.
/// </summary>
public static class SemanticOperationGraphSafetyValidator
{
    public static void Validate(
        SemanticOperationGraph graph,
        SecurityResourceLimits limits)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        var nodes = graph.Nodes.Count;
        if (nodes > limits.MaxOperationGraphNodes)
            Reject(
                $"Semantic operation graph exceeds the configured maximum of {limits.MaxOperationGraphNodes} nodes.");

        var edges = 0;
        var fields = 0;
        var maxDepth = 0;
        var visited = new HashSet<int>();

        Walk(graph.Root, depth: 1);

        if (edges > limits.MaxOperationGraphEdges)
            Reject(
                $"Semantic operation graph exceeds the configured maximum of {limits.MaxOperationGraphEdges} edges.");
        if (fields > limits.MaxOperationGraphFields)
            Reject(
                $"Semantic operation graph exceeds the configured maximum of {limits.MaxOperationGraphFields} fields.");

        void Walk(SemanticOperationGraphNode node, int depth)
        {
            if (!visited.Add(node.Id))
                Reject($"Semantic operation graph contains a repeated node '{node.Id}' or cycle.");

            if (depth > limits.MaxOperationGraphDepth)
                Reject(
                    $"Semantic operation graph depth exceeds the configured maximum of {limits.MaxOperationGraphDepth} levels.");

            maxDepth = Math.Max(maxDepth, depth);
            fields += node.Fields.Count + node.RequiredFields.Count;
            if (fields > limits.MaxOperationGraphFields)
                Reject(
                    $"Semantic operation graph exceeds the configured maximum of {limits.MaxOperationGraphFields} fields.");

            foreach (var childId in node.Children)
            {
                edges++;
                if (edges > limits.MaxOperationGraphEdges)
                    Reject(
                        $"Semantic operation graph exceeds the configured maximum of {limits.MaxOperationGraphEdges} edges.");

                var child = graph.GetNode(childId);
                if (child.ParentId != node.Id)
                    Reject(
                        $"Semantic operation graph edge '{node.Id}->{childId}' has an inconsistent parent reference.");
                Walk(child, depth + 1);
            }
        }

        if (visited.Count != nodes)
            Reject("Semantic operation graph contains unreachable nodes.");
    }

    private static void Reject(string message) => throw new InvalidOperationException(message);
}