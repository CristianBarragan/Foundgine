namespace Foundgine.Core.Execution.Mutation;

/// <summary>
/// Opaque physical mutation plan produced by a provider compiler.
/// The execution layer does not know the provider's mutation operations.
/// </summary>
public abstract record ProviderMutationPlan;