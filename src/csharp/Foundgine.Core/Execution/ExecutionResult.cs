using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Execution;

public sealed record ExecutionResult(
    IReadOnlyList<ExecutionRow> Rows,
    ExecutionPageInfo? PageInfo = null,
    ExecutionEvidence? Evidence = null,
    ExecutionReceipt? Receipt = null);

public sealed record ExecutionPageInfo(
    string? StartCursor,
    string? EndCursor,
    bool HasNextPage,
    bool HasPreviousPage);

/// <summary>
/// One row returned by a provider. Values retain their provider-facing names
/// for diagnostics/backwards compatibility, while Cells provide stable
/// provider-neutral identities for result materialization.
/// </summary>
public sealed record ExecutionRow(
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyDictionary<ExecutionCellKey, object?>? Cells = null)
{
    public IReadOnlyDictionary<ExecutionCellKey, object?> EffectiveCells => Cells ?? EmptyCells;

    private static readonly IReadOnlyDictionary<ExecutionCellKey, object?> EmptyCells =
        new Dictionary<ExecutionCellKey, object?>();
}

public readonly record struct ExecutionCellKey(
    int NodeId,
    EntityId EntityId,
    FieldId FieldId);
