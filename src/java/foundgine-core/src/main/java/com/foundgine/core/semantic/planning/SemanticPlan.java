package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.authorization.SemanticAuthorizationEvidence;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import java.util.*;

/** Canonical provider-neutral semantic planning artifact. */
public record SemanticPlan(SemanticPlanNode root, List<String> requiredSecurityInvariants,
		SemanticPlanAuthorizationBinding authorizationBinding) {
	public SemanticPlan {
		Objects.requireNonNull(root);
		requiredSecurityInvariants = requiredSecurityInvariants == null ? List.of()
				: List.copyOf(requiredSecurityInvariants);
	}

	public SemanticPlan(SemanticPlanNode root) {
		this(root, List.of(), null);
	}

	public List<String> effectiveSecurityInvariants() {
		return requiredSecurityInvariants;
	}
}
