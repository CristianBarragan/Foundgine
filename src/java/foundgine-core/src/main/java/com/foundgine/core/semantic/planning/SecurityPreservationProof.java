package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.security.SecurityInvariantRegistry;
import java.util.*;

/**
 * Records that a semantic rewrite preserved the security contract of the input
 * plan.
 */
public record SecurityPreservationProof(List<String> before, List<String> after, List<String> missing,
		String beforeFingerprint, String afterFingerprint) {
	public SecurityPreservationProof {
		before = before == null ? List.of() : List.copyOf(before);
		after = after == null ? List.of() : List.copyOf(after);
		missing = missing == null ? List.of() : List.copyOf(missing);
	}

	public boolean isSatisfied() {
		return missing.isEmpty() && before.stream().sorted().toList().equals(after.stream().sorted().toList());
	}

	public static SecurityPreservationProof create(SemanticPlan beforePlan, SemanticPlan afterPlan) {
		Objects.requireNonNull(beforePlan);
		Objects.requireNonNull(afterPlan);
		for (var id : beforePlan.effectiveSecurityInvariants())
			if (!SecurityInvariantRegistry.contains(id))
				throw new IllegalStateException("Unknown security invariant '" + id + "' in source plan.");
		for (var id : afterPlan.effectiveSecurityInvariants())
			if (!SecurityInvariantRegistry.contains(id))
				throw new IllegalStateException("Unknown security invariant '" + id + "' in rewritten plan.");
		var afterSet = new HashSet<>(afterPlan.effectiveSecurityInvariants());
		var missing = beforePlan.effectiveSecurityInvariants().stream().filter(id -> !afterSet.contains(id)).distinct()
				.sorted().toList();
		var proof = new SecurityPreservationProof(beforePlan.effectiveSecurityInvariants(),
				afterPlan.effectiveSecurityInvariants(), missing, SemanticPlanFingerprint.create(beforePlan),
				SemanticPlanFingerprint.create(afterPlan));
		if (!proof.isSatisfied())
			throw new IllegalStateException(
					"Security-preserving rewrite rejected. Missing invariants: " + String.join(", ", missing) + ".");
		return proof;
	}
}
