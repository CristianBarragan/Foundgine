namespace Foundgine.Core.Semantic.Planning.Mutation;

/// <summary>
/// Provider-neutral mutation batch plan with explicit dependency edges.
/// </summary>
public sealed record MutationBatchPlan(
    IReadOnlyList<MutationOperation> Operations,
    IReadOnlyList<MutationDependency> Dependencies);