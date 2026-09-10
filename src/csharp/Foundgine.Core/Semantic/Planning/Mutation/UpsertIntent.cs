using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Planning.Mutation;

/// <summary>
/// Provider-neutral upsert intent. ConflictColumns identify the logical
/// uniqueness key; when omitted the entity primary key is used.
/// ReturnFields are semantic fields requested from the resulting row.
/// </summary>
public sealed record UpsertIntent(
    EntityId Entity,
    IReadOnlyList<MutationFieldValue> Fields,
    IReadOnlyList<ColumnId>? ConflictColumns = null,
    IReadOnlyList<FieldId>? ReturnFields = null) : IMutationIntent
{
    public MutationKind Kind => MutationKind.Upsert;
}