package com.foundgine.core.semantic.resolution;

import java.util.List;

public record SemanticReferenceEvidence(
    String query,
    List<RetrievalCandidate> candidates,
    double confidence,
    String interpretation) {
    public SemanticReferenceEvidence {
        candidates = candidates == null ? List.of() : List.copyOf(candidates);
    }
}
