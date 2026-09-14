namespace Foundgine.Core.Abstractions;

/// <summary>
/// Narrow, provider-neutral schema contract required by mutation planning.
/// It deliberately exposes stable identities and key mappings only; concrete
/// metadata types remain outside the planning layer.
/// </summary>
public interface IMutationSchema
{
    MutationEntitySchema GetEntity(EntityId entityId);
    MutationRelationshipSchema GetRelationship(RelationshipId relationshipId);
}

public sealed record MutationEntitySchema(
    EntityId Id,
    string Name,
    IReadOnlySet<ColumnId> Columns,
    IReadOnlyDictionary<FieldId, ColumnId?> Fields,
    ColumnId? PrimaryKeyColumn);

public sealed record MutationRelationshipSchema(
    RelationshipId Id,
    EntityId Source,
    EntityId Target,
    string Name,
    ColumnId SourceColumn,
    ColumnId TargetColumn);