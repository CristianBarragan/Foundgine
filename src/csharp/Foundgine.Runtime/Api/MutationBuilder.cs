using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Mutation;

namespace Foundgine.Runtime;

/// <summary>Convenience entry point for advanced/open mutation authoring.</summary>
public static class FoundgineMutationExtensions
{
    /// <summary>
    /// Creates an open mutation builder over the application's semantic model.
    /// The resulting graph is executed by the supplied IFoundgineMutations instance,
    /// so authorization, invariants, approval and provider execution remain centralized.
    /// </summary>
    public static SemanticMutationIntentBuilder Mutate(
        this IFoundgineMutations mutations,
        SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        return new SemanticMutationIntentBuilder(model);
    }
}