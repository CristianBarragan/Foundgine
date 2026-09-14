using Foundgine.Core.Semantic.Planning.Mutation;

namespace Foundgine.Core.Execution.Mutation;

/// <summary>
/// Canonical provider-neutral execution dependency graph.
/// Dependency edges are already resolved to operation ordinals and physical
/// target columns. Provider-specific correlation carriers are introduced only
/// during provider lowering.
/// </summary>
public sealed class MutationDependencyGraph
{
    private readonly IReadOnlyList<MutationDependency> _dependencies;

    public IReadOnlyList<MutationDependency> Dependencies => _dependencies;

    public MutationDependencyGraph(IEnumerable<MutationDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _dependencies = dependencies.ToArray();
    }

    public IReadOnlySet<(int SourceOperationIndex, int TargetOperationIndex)> Edges =>
        _dependencies
            .Select(d => (d.SourceOperationIndex, d.TargetOperationIndex))
            .ToHashSet();
}