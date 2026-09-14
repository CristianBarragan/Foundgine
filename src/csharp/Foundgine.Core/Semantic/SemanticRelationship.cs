using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic;

/// <summary>
/// A named semantic relationship between two entities.
/// </summary>
public sealed record SemanticRelationship(
    RelationshipId Id,
    string Name,
    EntityId Target,
    RelationshipCardinality Cardinality,
    IReadOnlyList<SemanticAlias>? Aliases = null)
{
    public IReadOnlyList<SemanticAlias> EffectiveAliases => Aliases ?? [];
}
