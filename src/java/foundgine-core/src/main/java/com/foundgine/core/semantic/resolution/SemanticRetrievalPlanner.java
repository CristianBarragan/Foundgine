package com.foundgine.core.semantic.resolution;

import com.foundgine.core.semantic.SemanticField;

public final class SemanticRetrievalPlanner {
    private SemanticRetrievalPlanner() {}

    public static RetrievalStrategy select(SemanticField field) {
        return select(field, RetrievalStrategy.RELATIONAL);
    }

    /**
     * Selects a provider-neutral retrieval strategy. It never chooses a concrete
     * search/database provider.
     */
    public static RetrievalStrategy select(SemanticField field, RetrievalStrategy requested) {
        if (requested == RetrievalStrategy.RELATIONAL) return RetrievalStrategy.RELATIONAL;
        return switch (requested) {
            case FUZZY, FULL_TEXT, SEARCH, VECTOR -> requested;
            case GRAPH_SIMILARITY -> RetrievalStrategy.GRAPH_SIMILARITY;
            case RELATIONAL -> RetrievalStrategy.RELATIONAL;
        };
    }

    public static boolean requiresApproximateRetrieval(RetrievalStrategy strategy) {
        return switch (strategy) {
            case FULL_TEXT, SEARCH, FUZZY, VECTOR, GRAPH_SIMILARITY -> true;
            case RELATIONAL -> false;
        };
    }
}
