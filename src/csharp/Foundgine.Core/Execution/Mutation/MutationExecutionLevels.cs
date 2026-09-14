using Foundgine.Core.Semantic.Planning.Mutation;

namespace Foundgine.Core.Execution.Mutation;

/// <summary>
/// Provider-facing immutable dependency levels derived from the canonical
/// execution dependency graph.
/// </summary>
public sealed record MutationExecutionLevels(
    IReadOnlyList<IReadOnlyList<int>> Levels)
{
    public static MutationExecutionLevels From(
        int operationCount,
        IEnumerable<MutationDependency> dependencies)
        => new(MutationDependencyLevels.Compute(operationCount, dependencies));

    public static MutationExecutionLevels From(ExecutionMutationIR ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        return From(ir.Operations.Count, ir.Dependencies);
    }
}
