namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Optional provider boundary for approximate retrieval. Implementations may use
/// PostgreSQL FTS/trigram, Elasticsearch, OpenSearch, vector stores, graph indexes,
/// or another external system. The semantic layer has no dependency on any of them.
/// </summary>
public interface IApproximateCandidateSource
{
    IReadOnlyList<RetrievalCandidate> Retrieve(SemanticRetrievalRequest request);
}
