using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning.Mutation;

/// <summary>
/// Provider-neutral description of one entity mutation.
/// Update and Delete require a filter; Create does not.
/// </summary>
public sealed record MutationIntent(
    EntityId Entity,
    MutationKind Kind,
    IReadOnlyList<MutationFieldValue> Fields,
    SemanticFilterExpression? Filter = null,
    IReadOnlyList<FieldId>? ReturnFields = null) : IMutationIntent;