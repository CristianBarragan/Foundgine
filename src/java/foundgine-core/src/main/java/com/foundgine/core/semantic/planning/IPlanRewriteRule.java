package com.foundgine.core.semantic.planning;

import java.util.List;

/**
 * Contract for one provider-neutral semantic plan rewrite. Rules are
 * composable: they declare ordering constraints, conflicts, idempotence and
 * cost so the optimizer can build a deterministic rewrite path. Every accepted
 * application is independently checked for semantic and security preservation.
 */
public interface IPlanRewriteRule {
	String name();

	List<String> preconditions();

	List<String> securityObligations();

	double costImpact();

	default double benefitEstimate() {
		return 0d;
	}

	default List<String> mustRunAfter() {
		return List.of();
	}

	default List<String> mustRunBefore() {
		return List.of();
	}

	default List<String> conflictsWith() {
		return List.of();
	}

	default boolean isIdempotent() {
		return true;
	}

	default int priority() {
		return 0;
	}

	boolean canApply(SemanticPlan plan);

	SemanticPlan apply(SemanticPlan plan);
}
