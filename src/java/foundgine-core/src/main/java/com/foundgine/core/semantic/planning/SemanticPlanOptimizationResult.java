package com.foundgine.core.semantic.planning;

import java.util.*;

/** Result of provider-neutral semantic plan optimization. */
public record SemanticPlanOptimizationResult(SemanticPlan plan, List<String> appliedRules,
		SecurityPreservationProof securityProof, SemanticEquivalenceProof semanticProof,
		SemanticPlanAuthorizationBindingProof authorizationBindingProof, List<PlanRewriteRuleResult> ruleApplications,
		double totalCostImpact, boolean terminatedNormally) {
	public SemanticPlanOptimizationResult {
		appliedRules = List.copyOf(appliedRules);
		ruleApplications = ruleApplications == null ? List.of() : List.copyOf(ruleApplications);
	}

	public boolean changed() {
		return !appliedRules.isEmpty();
	}

	public List<PlanRewriteRuleResult> applications() {
		return ruleApplications;
	}
}
