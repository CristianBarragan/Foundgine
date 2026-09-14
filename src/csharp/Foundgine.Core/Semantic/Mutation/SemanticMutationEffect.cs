using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Mutation;

/// <summary>
/// One semantic effect associated with a mutation operation.
/// </summary>
public sealed record SemanticMutationEffect(
    SemanticMutationEffectKind Kind,
    EntityId Entity,
    FieldId? Field = null,
    RelationshipId? Relationship = null);
