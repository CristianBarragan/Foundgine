package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.EntityId;
import java.util.List;

public record ResolvedReference(EntityId entityType, String identityValue, double confidence, String reason,
		List<ResolutionEvidence> evidence) {
	public ResolvedReference {
		evidence = evidence == null ? List.of() : List.copyOf(evidence);
	}
}
