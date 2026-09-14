using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Execution.Mutation;

/// <summary>
/// Semantic result of a mutation tree. The tree mirrors the nested mutation
/// intent rather than the provider's flat operation list.
/// </summary>
public sealed record MutationMaterializedResult(
    IReadOnlyList<MutationMaterializedNode> Roots)
{
    public static MutationMaterializedResult Empty { get; } = new([]);
}

public sealed class MutationMaterializedNode
{
    private readonly Dictionary<RelationshipId, List<MutationMaterializedNode>> _children = [];

    public MutationMaterializedNode(
        int operationIndex,
        EntityId entityId,
        IReadOnlyDictionary<FieldId, object?> values)
    {
        OperationIndex = operationIndex;
        EntityId = entityId;
        Values = values;
    }

    public int OperationIndex { get; }
    public EntityId EntityId { get; }
    public IReadOnlyDictionary<FieldId, object?> Values { get; }

    public IReadOnlyDictionary<RelationshipId, IReadOnlyList<MutationMaterializedNode>> Children =>
        _children.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<MutationMaterializedNode>)x.Value);

    internal List<MutationMaterializedNode> GetChildren(RelationshipId relationshipId) =>
        _children.TryGetValue(relationshipId, out var children)
            ? children
            : _children[relationshipId] = [];
}