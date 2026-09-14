package com.foundgine.core.semantic.planning;

import java.util.Objects;

/**
 * Proves that a plan rewrite did not detach the executable plan from its
 * authorization binding.
 */
public record SemanticPlanAuthorizationBindingProof(SemanticPlanAuthorizationBinding before,
		SemanticPlanAuthorizationBinding after) {
	public boolean isSatisfied() {
		return equal(before, after);
	}

	public static SemanticPlanAuthorizationBindingProof create(SemanticPlan beforePlan, SemanticPlan afterPlan) {
		Objects.requireNonNull(beforePlan);
		Objects.requireNonNull(afterPlan);
		var proof = new SemanticPlanAuthorizationBindingProof(beforePlan.authorizationBinding(),
				afterPlan.authorizationBinding());
		if (!proof.isSatisfied())
			throw new IllegalStateException(
					"Plan rewrite rejected: authorization binding was added, removed, or changed by the rewrite.");
		return proof;
	}

	private static boolean equal(SemanticPlanAuthorizationBinding a, SemanticPlanAuthorizationBinding b) {
		if (a == b)
			return true;
		if (a == null || b == null)
			return false;
		return Objects.equals(a.contractFingerprint(), b.contractFingerprint())
				&& Objects.equals(a.authorizationFingerprint(), b.authorizationFingerprint());
	}
}
