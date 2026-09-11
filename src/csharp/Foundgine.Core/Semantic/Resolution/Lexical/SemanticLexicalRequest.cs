using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Provider-neutral lexical lookup request. A provider may be backed by
/// Elasticsearch, OpenSearch, a vector store, PostgreSQL FTS, or another
/// retrieval system. Context is advisory; the resolver remains authoritative
/// for semantic topology and path legality.
/// </summary>
public sealed record SemanticLexicalRequest(
    string Token,
    EntityId? ContextEntity = null,
    IReadOnlyList<SemanticLexicalCandidateKind>? Kinds = null,
    int Limit = 20)
{
    public IReadOnlyList<SemanticLexicalCandidateKind> EffectiveKinds =>
        Kinds ?? Enum.GetValues<SemanticLexicalCandidateKind>();
}