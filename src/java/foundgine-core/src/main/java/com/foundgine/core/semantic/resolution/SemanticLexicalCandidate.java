package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import java.util.List;

public record SemanticLexicalCandidate(
    String token, SemanticLexicalCandidateKind kind, String canonicalName, double score,
    EntityId entityId, RelationshipId relationshipId, FieldId fieldId,
    EntityId sourceEntityId, EntityId targetEntityId, String value,
    List<ResolutionEvidence> evidence) {
    public SemanticLexicalCandidate {
        evidence = evidence == null ? List.of() : List.copyOf(evidence);
    }
    public SemanticLexicalCandidate(String token, SemanticLexicalCandidateKind kind, String canonicalName, double score) {
        this(token, kind, canonicalName, score, null, null, null, null, null, null, List.of());
    }
    public List<ResolutionEvidence> effectiveEvidence() { return evidence; }
}
