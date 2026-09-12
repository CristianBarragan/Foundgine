package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.EntityId;
import java.util.List;

public record SemanticLexicalResolution(
    SemanticLexicalResolutionOutcome outcome, List<SemanticLexicalStep> steps, double confidence,
    EntityId rootEntity, String reason, List<SemanticLexicalCandidate> rootCandidates) {
    public SemanticLexicalResolution {
        steps = steps == null ? List.of() : List.copyOf(steps);
        rootCandidates = rootCandidates == null ? List.of() : List.copyOf(rootCandidates);
    }

    /**
     * Convenience five-argument overload defaulting {@code rootCandidates} to {@code null}
     * (then normalized to an empty list by the canonical constructor above).
     */
    public SemanticLexicalResolution(SemanticLexicalResolutionOutcome outcome, List<SemanticLexicalStep> steps,
            double confidence, EntityId rootEntity, String reason) {
        this(outcome, steps, confidence, rootEntity, reason, null);
    }
    public List<SemanticLexicalCandidate> effectiveRootCandidates() { return rootCandidates; }
}