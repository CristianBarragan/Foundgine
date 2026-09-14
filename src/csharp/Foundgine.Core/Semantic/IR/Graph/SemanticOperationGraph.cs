using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.IR.Graph;

/// <summary>
/// Immutable, provider-neutral operation graph produced from canonical Semantic IR.
/// The graph makes operation topology explicit so validation, authorization and
/// planning can inspect the requested computation without touching providers.
/// </summary>
public sealed class SemanticOperationGraph
{
    private readonly IReadOnlyDictionary<int, SemanticOperationGraphNode> _nodes;

    private SemanticOperationGraph(
        IReadOnlyDictionary<int, SemanticOperationGraphNode> nodes,
        int rootId)
    {
        _nodes = nodes;
        RootId = rootId;
    }

    public int RootId { get; }

    public IReadOnlyCollection<SemanticOperationGraphNode> Nodes => _nodes.Values.ToArray();

    public SemanticOperationGraphNode Root => _nodes[RootId];

    public SemanticOperationGraphNode GetNode(int id) =>
        _nodes.TryGetValue(id, out var node)
            ? node
            : throw new KeyNotFoundException($"Semantic operation graph does not contain node '{id}'.");

    /// <summary>
    /// Returns the canonical deterministic fingerprint of this semantic operation graph.
    /// </summary>
    public string Fingerprint() =>
        SemanticOperationGraphFingerprint.Create(this);

    public static SemanticOperationGraph Create(SemanticOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var nodes = new Dictionary<int, SemanticOperationGraphNode>();
        Build(operation.Root, nodes, parentId: null, isRoot: true);
        return new SemanticOperationGraph(nodes, operation.Root.Id);
    }

    /// <summary>
    /// Rebuilds canonical Semantic IR from this graph. This is deliberately a
    /// pure conversion: neither the graph nor its nodes are mutated.
    /// </summary>
    public SemanticOperation ToOperation()
    {
        var root = BuildOperationNode(RootId);
        return new SemanticOperation(root);
    }

    private SemanticReadNode BuildOperationNode(int id)
    {
        var node = GetNode(id);
        var children = node.Children.Select(BuildOperationNode).ToArray();

        return new SemanticReadNode(
            node.Id,
            node.EntityId,
            node.Fields.ToArray(),
            node.ViaRelationship,
            node.ViaConnection,
            children,
            node.QueryOptions,
            node.Authorization)
        {
            RequiredFields = node.RequiredFields.ToArray()
        };
    }

    private static void Build(
        SemanticReadNode node,
        IDictionary<int, SemanticOperationGraphNode> nodes,
        int? parentId,
        bool isRoot)
    {
        if (!nodes.TryAdd(
                node.Id,
                new SemanticOperationGraphNode(
                    node.Id,
                    node.EntityId,
                    node.Fields.ToArray(),
                    node.RequiredFields.ToArray(),
                    node.ViaRelationship,
                    node.ViaConnection,
                    node.Children.Select(x => x.Id).ToArray(),
                    parentId,
                    isRoot ? node.QueryOptions : null,
                    node.Authorization)))
        {
            throw new InvalidOperationException(
                $"Semantic operation graph contains a duplicate node id '{node.Id}'.");
        }

        if (!isRoot && node.ViaRelationship is null && node.ViaConnection is null)
            throw new InvalidOperationException(
                $"Non-root semantic node {node.Id} must specify the relationship or connection used to reach it.");

        if (node.ViaRelationship is not null && node.ViaConnection is not null)
            throw new InvalidOperationException(
                $"Semantic node {node.Id} cannot specify both a relationship and a connection.");

        if (isRoot && (node.ViaRelationship is not null || node.ViaConnection is not null))
            throw new InvalidOperationException(
                $"Root semantic node {node.Id} cannot specify a parent edge.");

        foreach (var child in node.Children)
            Build(child, nodes, node.Id, isRoot: false);
    }
}

/// <summary>
/// An immutable node in the semantic operation graph. Child identifiers are
/// explicit edges; provider/storage concepts are intentionally absent.
/// </summary>
public sealed class SemanticOperationGraphNode
{
    public SemanticOperationGraphNode(
        int id,
        EntityId entityId,
        IEnumerable<FieldId> fields,
        IEnumerable<FieldId> requiredFields,
        RelationshipId? viaRelationship,
        ConnectionId? viaConnection,
        IEnumerable<int> children,
        int? parentId,
        SemanticQueryOptions? queryOptions,
        AuthorizationPredicate? authorization)
    {
        Id = id;
        EntityId = entityId;
        Fields = Array.AsReadOnly(fields?.ToArray() ?? throw new ArgumentNullException(nameof(fields)));
        RequiredFields =
            Array.AsReadOnly(requiredFields?.ToArray() ?? throw new ArgumentNullException(nameof(requiredFields)));
        ViaRelationship = viaRelationship;
        ViaConnection = viaConnection;
        Children = Array.AsReadOnly(children?.ToArray() ?? throw new ArgumentNullException(nameof(children)));
        ParentId = parentId;
        QueryOptions = queryOptions;
        Authorization = authorization;
    }

    public int Id { get; }
    public EntityId EntityId { get; }
    public IReadOnlyList<FieldId> Fields { get; }
    public IReadOnlyList<FieldId> RequiredFields { get; }
    public RelationshipId? ViaRelationship { get; }
    public ConnectionId? ViaConnection { get; }
    public IReadOnlyList<int> Children { get; }
    public int? ParentId { get; }
    public SemanticQueryOptions? QueryOptions { get; }
    public AuthorizationPredicate? Authorization { get; }
    public bool IsRoot => ParentId is null;
}