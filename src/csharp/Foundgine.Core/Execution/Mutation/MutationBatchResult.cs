namespace Foundgine.Core.Execution.Mutation;

/// <summary>
/// Results from an atomic mutation batch, in execution order.
/// </summary>
public sealed record MutationBatchResult(
    IReadOnlyList<MutationResult> Results)
{
    public int TotalAffectedRows => Results.Sum(x => x.AffectedRows);
}