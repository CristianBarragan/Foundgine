namespace Foundgine.Core.Semantic.Resolution;

/// <summary>Provider-neutral retrieval capabilities. Foundgine does not prescribe the backing search technology.</summary>
public enum RetrievalStrategy : byte
{
    Relational,
    FullText,

    /// <summary>Relevance-oriented lexical retrieval such as BM25/pg_search.</summary>
    Search,
    Fuzzy,
    Vector,
    GraphSimilarity
}