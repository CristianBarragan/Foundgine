package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.security.*;
import java.util.*;

/**
 * Derives the minimum plan-level security contract from the authorized plan
 * shape.
 */
public final class SecurityInvariantPlanRequirements {
	private SecurityInvariantPlanRequirements() {
	}

	public static SemanticPlan attach(SemanticPlan plan) {
		return attach(plan, null);
	}

	public static SemanticPlan attach(SemanticPlan plan, Iterable<String> capabilityInvariants) {
		Objects.requireNonNull(plan);
		var ids = new HashSet<>(plan.effectiveSecurityInvariants());
		if (capabilityInvariants != null)
			for (var id : capabilityInvariants) {
				if (!SecurityInvariantRegistry.contains(id))
					throw new IllegalStateException("Unknown capability security invariant '" + id + "'.");
				ids.add(id);
			}
		collect(plan.root(), ids);
		if (ids.isEmpty())
			throw new IllegalStateException("A semantic plan cannot become executable without a security contract.");
		return new SemanticPlan(plan.root(), ids.stream().sorted().toList(), plan.authorizationBinding());
	}

	private static void collect(SemanticPlanNode node, Set<String> ids) {
		ids.add(SecurityInvariantIds.AUTHORIZATION_REQUIRED);
		ids.add(SecurityInvariantIds.PARAMETERIZED_VALUES);
		ids.add(SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION);
		if (!node.fields().isEmpty())
			ids.add(SecurityInvariantIds.FIELD_VISIBILITY);
		if (!node.children().isEmpty())
			ids.add(SecurityInvariantIds.RELATIONSHIP_VISIBILITY);
		if (node.authorization() != null)
			ids.add(SecurityInvariantIds.RUNTIME_AUTHORIZATION);
		node.children().forEach(c -> collect(c, ids));
	}
}
