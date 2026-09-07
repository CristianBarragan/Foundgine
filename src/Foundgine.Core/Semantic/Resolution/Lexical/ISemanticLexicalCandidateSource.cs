using System.Threading;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Retrieves lexical candidates across all semantic kinds. Implementations
/// must treat <see cref="SemanticLexicalRequest.Limit"/> as the maximum number
/// of candidates they return (subject to backend capabilities). The resolver
/// deliberately requests one candidate beyond its retention limit as a sentinel
/// so truncation remains observable even when the provider correctly enforces the
/// requested limit. Implementations that back retrieval with network or database
/// I/O (Elasticsearch, pgvector) should honor the <c>cancellationToken</c> in the cancellable
/// overload promptly: it is the only bound on retrieval latency in the
/// grounding pipeline — <see cref="SemanticLexicalResolver"/>'s own search
/// budget does not start counting until retrieval for every token has
/// already returned.
/// </summary>
public interface ISemanticLexicalCandidateSource
{
    IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request);

    /// <summary>Cancellable retrieval. Default implementation ignores the
    /// token and delegates to <see cref="Retrieve(SemanticLexicalRequest)"/>
    /// for source compatibility; implementations backed by I/O should
    /// override this to actually observe cancellation.</summary>
    IReadOnlyList<SemanticLexicalCandidate> Retrieve(
        SemanticLexicalRequest request,
        CancellationToken cancellationToken) => Retrieve(request);
}