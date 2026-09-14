using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// A lexical hypothesis returned by an approximate retrieval provider.
/// Score is provider relevance, not a probability and not an authorization
/// decision. Foundgine combines it with semantic graph compatibility.
/// </summary>
public sealed record SemanticLexicalCandidate(
    string Token,
    SemanticLexicalCandidateKind Kind,
    string CanonicalName,
    double Score,
    EntityId? EntityId = null,
    RelationshipId? RelationshipId = null,
    FieldId? FieldId = null,
    EntityId? SourceEntityId = null,
    EntityId? TargetEntityId = null,
    string? Value = null,
    IReadOnlyList<ResolutionEvidence>? Evidence = null)
{
    public IReadOnlyList<ResolutionEvidence> EffectiveEvidence => Evidence ?? [];
}
