package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.resolution.*;
import java.util.*;

/** Measures declared alias-weight evidence that actually participated in lexical grounding. */
public final class AliasWeightEvidenceGate {
    private AliasWeightEvidenceGate() {}

    public enum AliasEvidenceStatus { NOT_APPLICABLE, SUFFICIENT, INSUFFICIENT }
    public enum ModelResolutionEvidence { UNKNOWN, KNOWN_WITH_CERTAINTY }

    public record AliasWeightEvidenceResult(
        AliasEvidenceStatus status,
        ModelResolutionEvidence modelEvidence,
        Map<EntityId,Integer> entityWeights,
        Map<FieldId,Integer> fieldWeights,
        Map<RelationshipId,Integer> relationshipWeights,
        List<EntityId> violatingEntities,
        List<FieldId> violatingFields,
        List<RelationshipId> violatingRelationships,
        String contractFingerprint) {
        public AliasWeightEvidenceResult {
            entityWeights = entityWeights == null ? Map.of() : Map.copyOf(entityWeights);
            fieldWeights = fieldWeights == null ? Map.of() : Map.copyOf(fieldWeights);
            relationshipWeights = relationshipWeights == null ? Map.of() : Map.copyOf(relationshipWeights);
            violatingEntities = violatingEntities == null ? List.of() : List.copyOf(violatingEntities);
            violatingFields = violatingFields == null ? List.of() : List.copyOf(violatingFields);
            violatingRelationships = violatingRelationships == null ? List.of() : List.copyOf(violatingRelationships);
        }
        /** Compatibility projection retained for parity with the C# obsolete API. */
        @Deprecated public boolean isConclusive() { return status != AliasEvidenceStatus.INSUFFICIENT; }
        /** Compatibility projection; model provenance is not a lexical score. */
        @Deprecated public Integer modelWeight() { return modelEvidence == ModelResolutionEvidence.KNOWN_WITH_CERTAINTY ? 100 : null; }
    }

    public static AliasWeightEvidenceResult evaluate(SemanticModel model, int minimumWeight,
                                                      SemanticLexicalResolution lexicalResolution,
                                                      boolean modelKnownWithCertainty) {
        Objects.requireNonNull(model); model.ensureFrozen();
        return evaluate(model.createSnapshot(), minimumWeight, lexicalResolution, modelKnownWithCertainty);
    }
    public static AliasWeightEvidenceResult evaluate(SemanticModel model, int minimumWeight) {
        return evaluate(model, minimumWeight, null, false);
    }
    public static AliasWeightEvidenceResult evaluate(SemanticContractSnapshot model, int minimumWeight,
                                                      SemanticLexicalResolution lexicalResolution,
                                                      boolean modelKnownWithCertainty) {
        Objects.requireNonNull(model);
        if (minimumWeight < 1 || minimumWeight > 100) throw new IllegalArgumentException("minimumWeight must be between 1 and 100 (inclusive).");
        var modelEvidence = modelKnownWithCertainty ? ModelResolutionEvidence.KNOWN_WITH_CERTAINTY : ModelResolutionEvidence.UNKNOWN;
        if (lexicalResolution == null || lexicalResolution.steps().isEmpty())
            return new AliasWeightEvidenceResult(AliasEvidenceStatus.NOT_APPLICABLE, modelEvidence, Map.of(), Map.of(), Map.of(), List.of(), List.of(), List.of(), model.contractFingerprint());

        var ew = new HashMap<EntityId,Integer>(); var fw = new HashMap<FieldId,Integer>(); var rw = new HashMap<RelationshipId,Integer>();
        for (var step : lexicalResolution.steps()) {
            var c = step.candidate();
            switch (c.kind()) {
                case ENTITY, NODE -> { if (c.entityId()!=null) model.aliasWeight(c.entityId(), c.token()).ifPresent(v -> ew.merge(c.entityId(), v, Math::max)); }
                case FIELD -> { if (c.entityId()!=null && c.fieldId()!=null) model.aliasWeight(c.entityId(), c.fieldId(), c.token()).ifPresent(v -> fw.merge(c.fieldId(), v, Math::max)); }
                case RELATIONSHIP -> { if (c.relationshipId()!=null) model.aliasWeight(c.relationshipId(), c.token()).ifPresent(v -> rw.merge(c.relationshipId(), v, Math::max)); }
                case TRAVERSAL, VALUE, OPERATION -> { }
            }
        }
        var ve = ew.entrySet().stream().filter(x->x.getValue()<minimumWeight).map(Map.Entry::getKey).toList();
        var vf = fw.entrySet().stream().filter(x->x.getValue()<minimumWeight).map(Map.Entry::getKey).toList();
        var vr = rw.entrySet().stream().filter(x->x.getValue()<minimumWeight).map(Map.Entry::getKey).toList();
        var applicable = !ew.isEmpty() || !fw.isEmpty() || !rw.isEmpty();
        var status = !applicable ? AliasEvidenceStatus.NOT_APPLICABLE : (ve.isEmpty() && vf.isEmpty() && vr.isEmpty() ? AliasEvidenceStatus.SUFFICIENT : AliasEvidenceStatus.INSUFFICIENT);
        return new AliasWeightEvidenceResult(status, modelEvidence, ew, fw, rw, ve, vf, vr, model.contractFingerprint());
    }
    public static AliasWeightEvidenceResult evaluate(SemanticContractSnapshot model, int minimumWeight) { return evaluate(model, minimumWeight, null, false); }

    public record AliasInterpretationEvidence(AliasEvidenceStatus status, Map<EntityId,Integer> entityWeights,
                                               Map<FieldId,Integer> fieldWeights, Map<RelationshipId,Integer> relationshipWeights,
                                               String contractFingerprint) {
        public static AliasInterpretationEvidence from(AliasWeightEvidenceResult result) {
            return new AliasInterpretationEvidence(result.status(), result.entityWeights(), result.fieldWeights(), result.relationshipWeights(), result.contractFingerprint());
        }
    }
}
