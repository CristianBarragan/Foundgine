using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.IR;

/// <summary>
/// Canonical, provider-neutral representation of one resolved semantic
/// operation. This is the boundary between semantic graph resolution and
/// planning. It contains meaning, not storage or provider instructions.
/// </summary>
public sealed record SemanticOperation(SemanticReadNode Root)
{
    public bool IsReadOnly => true;
}

/// <summary>
/// A semantic read node. Nodes describe domain traversal and selected fields;
/// providers and storage systems are deliberately absent.
/// </summary>
public sealed record SemanticReadNode(
    int Id,
    EntityId EntityId,
    IReadOnlyList<FieldId> Fields,
    RelationshipId? ViaRelationship,
    ConnectionId? ViaConnection,
    IReadOnlyList<SemanticReadNode> Children,
    SemanticQueryOptions? QueryOptions = null,
    AuthorizationPredicate? Authorization = null)
{
    /// <summary>Fields required internally by predicates/order/dependencies but not necessarily returned.</summary>
    public IReadOnlyList<FieldId> RequiredFields { get; init; } = [];

    public bool IsRoot => ViaRelationship is null && ViaConnection is null;
}