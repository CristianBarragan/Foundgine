package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import java.util.List;

public record RetrievalCandidate(
    EntityId entityType, String recordId, double score, FieldId matchedField,
    String identityValue, List<ResolutionEvidence> evidence, CandidateEvidenceKind evidenceKind) {
    public RetrievalCandidate {
        evidence = evidence == null ? List.of() : List.copyOf(evidence);
    }
    public RetrievalCandidate(EntityId entityType, String recordId, double score) {
        this(entityType, recordId, score, null, null, List.of(), null);
    }
    public List<ResolutionEvidence> effectiveEvidence() { return evidence; }
}
