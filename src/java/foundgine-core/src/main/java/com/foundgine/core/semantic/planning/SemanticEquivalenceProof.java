package com.foundgine.core.semantic.planning;

import java.util.Objects;

/**
 * Records that a rewrite preserved the provider-neutral semantic meaning of a
 * plan.
 */
public record SemanticEquivalenceProof(String beforeFingerprint, String afterFingerprint) {
	public boolean isSatisfied() {
		return Objects.equals(beforeFingerprint, afterFingerprint);
	}

	public static SemanticEquivalenceProof create(SemanticPlan before, SemanticPlan after) {
		Objects.requireNonNull(before, "before");
		Objects.requireNonNull(after, "after");
		var proof = new SemanticEquivalenceProof(SemanticEquivalenceFingerprint.create(before),
				SemanticEquivalenceFingerprint.create(after));
		if (!proof.isSatisfied())
			throw new IllegalStateException(
					"Semantic rewrite rejected because the rewritten plan does not preserve the canonical semantic meaning of the source plan.");
		return proof;
	}
}
