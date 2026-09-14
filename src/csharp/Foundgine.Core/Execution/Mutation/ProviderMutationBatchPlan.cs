namespace Foundgine.Core.Execution.Mutation;

/// <summary>
/// Opaque provider batch plan. Ordering, dependencies, and physical
/// representation belong to the provider-specific plan.
/// </summary>
public abstract record ProviderMutationBatchPlan(
    IReadOnlyList<ProviderMutationPlan> Operations);
