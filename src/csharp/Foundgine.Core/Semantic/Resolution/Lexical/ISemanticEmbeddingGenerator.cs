namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Provider-neutral text-embedding boundary. A vector-backed
/// <see cref="ISemanticLexicalCandidateSource"/> depends on this instead of a
/// specific embedding model, vendor SDK, or hosting environment. Foundgine's
/// semantic layer has no dependency on any embedding provider; it only
/// consumes the vectors this interface produces.
/// </summary>
public interface ISemanticEmbeddingGenerator
{
    /// <summary>Embeds a single piece of text, such as a query token.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Embeds many pieces of text in one call. Implementations should batch
    /// this against the underlying model where possible; callers use it when
    /// indexing a full <see cref="SemanticLexiconEntry"/> projection.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedManyAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}