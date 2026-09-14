using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Mutation;

/// <summary>
/// Canonical semantic mutation graph. The graph describes what a mutation means
/// and how its effects depend on one another; physical execution is a later lowering.
/// </summary>
public sealed record SemanticMutationOperationGraph(
    IReadOnlyList<SemanticMutationOperation> Operations)
{
    public IEnumerable<SemanticMutationEffect> Effects =>
        Operations.SelectMany(x => x.Effects);
}