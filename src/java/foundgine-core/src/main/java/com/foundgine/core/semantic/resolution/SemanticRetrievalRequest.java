package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import java.util.List;

public record SemanticRetrievalRequest(
    EntityId entityType,
    FieldId field,
    String query,
    RetrievalStrategy strategy,
    int limit,
    EntityId sourceEntity,
    RelationshipId relationship,
    String referenceIdentity) {
    public SemanticRetrievalRequest {
        if (query == null || query.isBlank()) throw new IllegalArgumentException("Retrieval query cannot be empty.");
        if (limit < 1 || limit > 1000) throw new IllegalArgumentException("Retrieval limit must be between 1 and 1000.");
        if (strategy == null) throw new NullPointerException("strategy");
    }
    public SemanticRetrievalRequest(EntityId entityType, FieldId field, String query, RetrievalStrategy strategy) {
        this(entityType, field, query, strategy, 10, null, null, null);
    }
    public SemanticRetrievalRequest(EntityId entityType, FieldId field, String query, RetrievalStrategy strategy, int limit) {
        this(entityType, field, query, strategy, limit, null, null, null);
    }
}
