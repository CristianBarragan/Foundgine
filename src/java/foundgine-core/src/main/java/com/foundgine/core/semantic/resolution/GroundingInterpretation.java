package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.semantic.AliasWeightEvidenceGate.AliasEvidenceStatus;
import com.foundgine.core.semantic.AliasWeightEvidenceGate.AliasInterpretationEvidence;
import java.util.*;

public record GroundingInterpretation(List<SemanticLexicalStep> steps, double interpretationScore, EntityId rootEntity,
		String signature, AliasInterpretationEvidence aliasEvidence) {

	public GroundingInterpretation {
		steps = steps == null ? List.of() : List.copyOf(steps);
	}

	@Deprecated
	public double confidence() {
		return interpretationScore;
	}

	public List<ResolutionEvidence> lexicalEvidence() {
		return steps.stream().flatMap(x -> x.candidate().effectiveEvidence().stream()).toList();
	}

	public AliasInterpretationEvidence effectiveAliasEvidence() {
		return aliasEvidence != null ? aliasEvidence
				: new AliasInterpretationEvidence(AliasEvidenceStatus.NOT_APPLICABLE, Map.of(), Map.of(), Map.of(), "");
	}
}
