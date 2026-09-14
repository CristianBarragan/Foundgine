namespace Foundgine.Core.Semantic.Planning.Mutation;

/// <summary>
/// Ordered mutation operations that execute atomically. Field references may point
/// only to earlier operations in the batch.
/// </summary>
public sealed record MutationBatchIntent(
    IReadOnlyList<IMutationIntent> Operations);