using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic;

/// <summary>
/// Proves that a request graph is consistent with the semantic model. Graph
/// construction remains available for low-level integrations, but every graph
/// produced by the resolver is validated before it leaves the resolution stage.
/// </summary>
public static class SemanticGraphValidator
{
    public static void Validate(SemanticGraph graph, SemanticContractSnapshot contract,
        SemanticGraphValidationMode mode = SemanticGraphValidationMode.Strict)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(contract);
        ValidateCore(graph, contract.TryGet, contract.Get, mode);
    }

    public static void Validate(SemanticGraph graph, SemanticModel model,
        SemanticGraphValidationMode mode = SemanticGraphValidationMode.Strict)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(model);
        ValidateCore(graph, model.TryGet, model.Get, mode);
    }

    private static void ValidateCore(
        SemanticGraph graph,
        TryGetEntity tryGetEntity,
        Func<EntityId, SemanticEntity> getEntity,
        SemanticGraphValidationMode mode)
    {
        if (graph.Nodes.Count == 0)
            throw new InvalidOperationException("A semantic graph cannot be empty.");

        var ids = graph.Nodes.Select(x => x.Id).ToArray();
        if (ids.Length != ids.Distinct().Count())
            throw new InvalidOperationException("A semantic graph contains duplicate node identities.");
        var idSet = ids.ToHashSet();
        var roots = graph.Nodes.Where(x => x.ParentId is null).ToArray();
        if (mode == SemanticGraphValidationMode.Strict && roots.Length != 1)
            throw new InvalidOperationException("A semantic graph must contain exactly one root node.");
        if (mode != SemanticGraphValidationMode.Strict && roots.Length == 0)
            throw new InvalidOperationException("A semantic graph must contain at least one root node.");

        foreach (var node in graph.Nodes)
        {
            var entityFound = tryGetEntity(node.EntityId, out var entity);

            if (node.ParentId is { } parentId)
            {
                if (!idSet.Contains(parentId))
                    throw new InvalidOperationException(
                        $"Semantic node {node.Id} references missing parent node {parentId}.");

                var parent = graph.Nodes.First(x => x.Id == parentId);
                if (node.ViaRelationship is null && node.ViaConnection is null)
                    throw new InvalidOperationException(
                        $"Non-root semantic node {node.Id} must specify a relationship or connection.");

                if (node.ViaRelationship is { } relationshipId)
                {
                    if (node.ViaConnection is not null)
                        throw new InvalidOperationException(
                            $"Semantic node {node.Id} cannot specify both relationship and connection edges.");

                    var parentEntity = getEntity(parent.EntityId);
                    var relationship = parentEntity.Relationships.FirstOrDefault(x => x.Id == relationshipId);
                    if (relationship is null)
                    {
                        if (mode is SemanticGraphValidationMode.Federated or SemanticGraphValidationMode.Exploratory)
                            continue;
                        throw new InvalidOperationException(
                            $"Parent entity '{parentEntity.Name}' does not declare relationship '{relationshipId}' for semantic node {node.Id}.");
                    }

                    if (relationship.Target != node.EntityId)
                    {
                        var targetName = tryGetEntity(relationship.Target, out var targetEntity)
                            ? targetEntity.Name
                            : relationship.Target.ToString();
                        throw new InvalidOperationException(
                            $"Semantic node {node.Id} targets entity '{node.EntityId}', but relationship '{relationship.Name}' targets '{targetName}'.");
                    }
                }
            }
            else if (node.ViaRelationship is not null || node.ViaConnection is not null)
            {
                throw new InvalidOperationException($"Root semantic node {node.Id} cannot specify a parent edge.");
            }

            if (!entityFound)
            {
                if (mode is SemanticGraphValidationMode.Federated or SemanticGraphValidationMode.Exploratory)
                    continue;
                throw new InvalidOperationException(
                    $"Semantic node {node.Id} references unknown entity '{node.EntityId}'.");
            }

            foreach (var fieldId in node.Fields.Distinct())
            {
                if (fieldId != entity.Identity.FieldId && entity.Fields.All(x => x.Id != fieldId))
                {
                    if (mode is SemanticGraphValidationMode.Exploratory)
                        continue;
                    throw new InvalidOperationException(
                        $"Semantic node {node.Id} selects unknown field '{fieldId}' on '{entity.Name}'.");
                }
            }
        }

        // Every node must be reachable from a root. Strict mode has exactly one root;
        // loose/federated/exploratory modes may intentionally have several.
        var children = graph.Nodes.ToLookup(x => x.ParentId);
        var visited = new HashSet<int>();
        foreach (var root in roots)
            Visit(root, children, visited);
        if (visited.Count != graph.Nodes.Count && mode != SemanticGraphValidationMode.Exploratory)
            throw new InvalidOperationException("Semantic graph contains unreachable nodes.");
    }

    private static void Visit(SemanticGraphNode node, ILookup<int?, SemanticGraphNode> children, ISet<int> visited)
    {
        if (!visited.Add(node.Id))
            throw new InvalidOperationException($"Semantic graph contains a cycle at node {node.Id}.");
        foreach (var child in children[node.Id])
            Visit(child, children, visited);
    }

    private delegate bool TryGetEntity(EntityId id, out SemanticEntity entity);
}