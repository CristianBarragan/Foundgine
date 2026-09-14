using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Chooses whether a predicate can remain relational or requires approximate retrieval.
/// It deliberately does not choose a concrete provider.
/// </summary>
public static class SemanticRetrievalPlanner
{
    public static RetrievalStrategy Select(
        SemanticField field,
        RetrievalStrategy requested = RetrievalStrategy.Relational)
    {
        if (requested == RetrievalStrategy.Relational)
            return RetrievalStrategy.Relational;

        if (requested is RetrievalStrategy.Fuzzy or RetrievalStrategy.FullText or RetrievalStrategy.Search
            or RetrievalStrategy.Vector)
            return requested;

        return RetrievalStrategy.GraphSimilarity;
    }

    public static bool RequiresApproximateRetrieval(RetrievalStrategy strategy) =>
        strategy is RetrievalStrategy.FullText
            or RetrievalStrategy.Search
            or RetrievalStrategy.Fuzzy
            or RetrievalStrategy.Vector
            or RetrievalStrategy.GraphSimilarity;
}