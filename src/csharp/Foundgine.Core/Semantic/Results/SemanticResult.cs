using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Results;

/// <summary>
/// Provider-independent result of a resolved semantic operation.
/// Transport adapters may reshape this tree, but the semantic topology and
/// selected field values remain stable.
/// </summary>
public sealed record SemanticResult(
    IReadOnlyList<SemanticResultNode> Roots,
    SemanticResultPageInfo? PageInfo = null,
    SemanticResultEvidence? Evidence = null);

public sealed class SemanticResultNode
{
    private readonly Dictionary<RelationshipId, List<SemanticResultNode>> _children = [];

    public SemanticResultNode(
        int planNodeId,
        EntityId entityId,
        object identityValue,
        IReadOnlyDictionary<FieldId, object?> values)
    {
        ArgumentNullException.ThrowIfNull(identityValue);
        PlanNodeId = planNodeId;
        EntityId = entityId;
        IdentityValue = identityValue;
        Values = values ?? throw new ArgumentNullException(nameof(values));
    }

    public int PlanNodeId { get; }
    public EntityId EntityId { get; }
    public object IdentityValue { get; }
    public IReadOnlyDictionary<FieldId, object?> Values { get; }

    public IReadOnlyDictionary<RelationshipId, IReadOnlyList<SemanticResultNode>> Children =>
        _children.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<SemanticResultNode>)x.Value);

    internal List<SemanticResultNode> GetChildren(RelationshipId relationshipId) =>
        _children.TryGetValue(relationshipId, out var children)
            ? children
            : _children[relationshipId] = [];
}

/// <summary>Semantic pagination state, independent of a transport protocol.</summary>
public sealed record SemanticResultPageInfo(
    string? StartCursor,
    string? EndCursor,
    bool HasNextPage,
    bool HasPreviousPage);

/// <summary>
/// Optional provenance retained after execution without exposing provider
/// objects or transport-specific result types.
/// </summary>
public sealed record SemanticResultEvidence(
    string Provider,
    string PlanFingerprint,
    IReadOnlyList<int> AuthorizedNodeIds,
    int RowsReturned,
    long ElapsedMilliseconds,
    string? ProviderOperationFingerprint = null,
    string? IntentFingerprint = null,
    string? AuthorizationFingerprint = null);