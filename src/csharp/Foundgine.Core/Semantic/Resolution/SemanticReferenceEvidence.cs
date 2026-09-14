namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Provider-neutral evidence used to ground natural-language references in the semantic model.
/// Retrieval providers produce evidence; the semantic layer decides whether it is sufficient to resolve.
/// </summary>
public sealed record SemanticReferenceEvidence(
    string Query,
    IReadOnlyList<RetrievalCandidate> Candidates,
    double Confidence,
    string Interpretation);
