using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Planning.Mutation;

/// <summary>
/// A mutation field value. The value may be supplied directly or referenced from
/// a returned field of an earlier operation in the same mutation batch.
/// </summary>
public sealed record MutationFieldValue(
    ColumnId Column,
    object? Value,
    MutationValueReference? Source = null)
{
    // Compatibility name for consumers that address the physical mutation field as a column.
    public ColumnId ColumnId => Column;

    public static MutationFieldValue FromPrevious(
        ColumnId column,
        int sourceOperationIndex,
        FieldId sourceField) =>
        new(column, null, new MutationValueReference(sourceOperationIndex, sourceField));
}