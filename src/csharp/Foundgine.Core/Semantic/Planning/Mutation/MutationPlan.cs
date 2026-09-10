namespace Foundgine.Core.Semantic.Planning.Mutation;

public sealed record MutationPlan(
    IReadOnlyList<MutationOperation> Operations);