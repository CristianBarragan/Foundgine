package com.foundgine.core.semantic.planning;

import java.util.*;

/** Conservative effect summary derived only from the semantic plan. */
public record PlanEffectSummary(boolean hasWrites, boolean hasExternalSideEffects, int affectedPlanNodes,
		List<String> effects) {
	public PlanEffectSummary {
		effects = effects == null ? List.of() : List.copyOf(effects);
	}
}
