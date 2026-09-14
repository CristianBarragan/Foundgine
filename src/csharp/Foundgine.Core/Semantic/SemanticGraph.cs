using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic;

/// <summary>
/// The canonical request graph. It contains only semantic/domain topology.
/// It must not contain SQL, GraphQL, provider nodes, aliases, or storage SQL.
/// A node may be reached through either a relational relationship or an AOT
/// semantic connection. A connection is a pre-resolved communication edge.
/// </summary>
public sealed class SemanticGraph
{
    private readonly List<SemanticGraphNode> _nodes = [];

    /// <summary>Creates an empty semantic graph for incremental construction and tests.</summary>
    public SemanticGraph()
    {
    }

    internal SemanticGraph(IEnumerable<SemanticGraphNode> nodes,
        Foundgine.Core.Semantic.Query.SemanticQueryOptions? options)
    {
        _nodes.AddRange(nodes);
        Options = options;
    }

    public IReadOnlyList<SemanticGraphNode> Nodes => _nodes;

    public Foundgine.Core.Semantic.Query.SemanticQueryOptions? Options { get; internal set; }

    /// <summary>
    /// Returns a semantic graph with an authorization predicate attached to an
    /// existing node. The graph topology and query options are preserved.
    /// This is the boundary used when policy is supplied after intent
    /// resolution but before planning.
    /// </summary>
    public SemanticGraph WithAuthorization(int nodeId, AuthorizationPredicate authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        if (_nodes.All(node => node.Id != nodeId))
            throw new ArgumentOutOfRangeException(nameof(nodeId));

        // Preserve node identities and topology. Only the affected immutable
        // record is replaced; this avoids rebuilding the entire graph.
        var nodes = _nodes
            .Select(node => node.Id == nodeId ? node with { Authorization = authorization } : node)
            .ToArray();

        return new SemanticGraph(nodes, Options);
    }

    public SemanticGraphNode AddRoot(
        EntityId entityId,
        IEnumerable<FieldId>? fields = null,
        AuthorizationPredicate? authorization = null) =>
        Add(entityId, null, null, null, fields, authorization);

    /// <summary>
    /// Adds a node reached through an AOT semantic connection. The connection
    /// identifies the pre-resolved communication edge; the target entity is
    /// already known and remains the only storage-side identity in the graph.
    /// </summary>
    public SemanticGraphNode AddConnection(
        EntityId entityId,
        ConnectionId connectionId,
        SemanticGraphNode parent,
        IEnumerable<FieldId>? fields = null,
        AuthorizationPredicate? authorization = null) =>
        Add(entityId, null, connectionId, parent, fields, authorization);

    public SemanticGraphNode Add(
        EntityId entityId,
        RelationshipId? relationshipId,
        SemanticGraphNode? parent,
        IEnumerable<FieldId>? fields = null) =>
        Add(entityId, relationshipId, parent, fields, null);

    /// <summary>
    /// Adds a node reached through a semantic relationship while preserving
    /// the AOT authorization predicate attached to that node.
    /// </summary>
    public SemanticGraphNode Add(
        EntityId entityId,
        RelationshipId? relationshipId,
        SemanticGraphNode? parent,
        IEnumerable<FieldId>? fields,
        AuthorizationPredicate? authorization) =>
        Add(entityId, relationshipId, null, parent, fields, authorization);

    private SemanticGraphNode Add(
        EntityId entityId,
        RelationshipId? relationshipId,
        ConnectionId? connectionId,
        SemanticGraphNode? parent,
        IEnumerable<FieldId>? fields = null,
        AuthorizationPredicate? authorization = null)
    {
        if (relationshipId is not null && connectionId is not null)
            throw new ArgumentException(
                "A semantic node cannot be reached through both a relationship and a connection.");

        if (parent is null && connectionId is not null)
            throw new ArgumentException("A root semantic node cannot be reached through a connection.");

        var node = new SemanticGraphNode(
            _nodes.Count,
            entityId,
            relationshipId,
            connectionId,
            parent?.Id,
            authorization)
        {
            Fields = fields?.Distinct().ToArray() ?? []
        };

        _nodes.Add(node);
        return node;
    }
}

public sealed record SemanticGraphNode(
    int Id,
    EntityId EntityId,
    RelationshipId? ViaRelationship,
    ConnectionId? ViaConnection,
    int? ParentId,
    AuthorizationPredicate? Authorization = null)
{
    /// <summary>Stable semantic annotations supplied by intent or semantic configuration.</summary>
    public IReadOnlyDictionary<string, string> SemanticAnnotations { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Provenance for how this node was traversed from the root.</summary>
    public SemanticTraversalOrigin? TraversalOrigin { get; init; }

    /// <summary>Provenance identifying the intent operation that created the node.</summary>
    public SemanticIntentOrigin? IntentOrigin { get; init; }

    /// <summary>Cardinality expected by the intent/planner at this node.</summary>
    public SemanticExpectedCardinality ExpectedCardinality { get; init; } = SemanticExpectedCardinality.Unknown;

    /// <summary>Whether the path can legally produce a null target.</summary>
    public bool IsNullablePath { get; init; }

    /// <summary>Provider-neutral semantic constraints relevant to this node.</summary>
    public IReadOnlyList<SemanticConstraint> SemanticConstraints { get; init; } = [];

    /// <summary>
    /// Fields selected for this entity in the request. These are semantic
    /// field identities only; no provider/storage information is carried.
    /// </summary>
    public IReadOnlyList<FieldId> Fields { get; init; } = [];
}