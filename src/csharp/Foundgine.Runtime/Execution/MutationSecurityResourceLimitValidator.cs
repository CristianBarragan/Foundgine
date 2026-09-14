using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Security.Execution;

namespace Foundgine.Runtime;

/// <summary>
/// Canonical mutation-side resource guard. It runs before mutation planning,
/// authorization, provider lowering, or replay consumption.
/// </summary>
public static class MutationSecurityResourceLimitValidator
{
    public static void Validate(SemanticMutationRequest request, SecurityResourceLimits limits)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        ArgumentNullException.ThrowIfNull(request.Graph);

        var operations = request.Graph.Operations;
        if (operations.Count == 0)
            Reject("A semantic mutation graph must contain at least one operation.");
        if (operations.Count > limits.MaxMutationOperations)
            Reject($"Mutation operation count exceeds the configured maximum of {limits.MaxMutationOperations}.");

        var dependencyCount = operations.Sum(x => x.Dependencies.Count);
        if (dependencyCount > limits.MaxMutationDependencies)
            Reject($"Mutation dependency count exceeds the configured maximum of {limits.MaxMutationDependencies}.");

        var effectCount = operations.Sum(x => x.Effects.Count);
        if (effectCount > limits.MaxMutationEffects)
            Reject($"Mutation effect count exceeds the configured maximum of {limits.MaxMutationEffects}.");

        for (var i = 0; i < operations.Count; i++)
        {
            var operation = operations[i];
            if (operation.Fields.Count > limits.MaxMutationFieldsPerOperation)
                Reject(
                    $"Mutation field count for operation {i} exceeds the configured maximum of {limits.MaxMutationFieldsPerOperation}.");
            if (operation.ReturnFields.Count > limits.MaxMutationReturnFieldsPerOperation)
                Reject(
                    $"Mutation return-field count for operation {i} exceeds the configured maximum of {limits.MaxMutationReturnFieldsPerOperation}.");

            if (operation.Filter is not null)
                SecurityResourceLimitValidator.ValidateFilter(operation.Filter, limits);
        }
    }

    private static void Reject(string message) => throw new InvalidOperationException(message);
}