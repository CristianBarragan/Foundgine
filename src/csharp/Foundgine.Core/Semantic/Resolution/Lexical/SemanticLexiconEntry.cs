using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Provider-neutral document that can be projected into a lexical search
/// index. The semantic contract is the source of truth; the index is a derived
/// retrieval projection.
/// </summary>
public sealed record SemanticLexiconEntry(
    string CanonicalName,
    SemanticLexicalCandidateKind Kind,
    string SearchText,
    EntityId? EntityId = null,
    RelationshipId? RelationshipId = null,
    FieldId? FieldId = null,
    EntityId? SourceEntityId = null,
    EntityId? TargetEntityId = null,
    string? Value = null,
    IReadOnlyList<string>? Aliases = null,
    string? Description = null)
{
    public IReadOnlyList<string> EffectiveAliases => Aliases ?? [];
}