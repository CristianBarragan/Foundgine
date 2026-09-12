package com.foundgine.core.semantic.planning;

import java.util.*;

/** Applies a deterministic composition of provider-neutral rewrite rules. */
public final class SemanticPlanOptimizer implements IPlanOptimizer {
	private final RewriteRuleComposer composer;

	public SemanticPlanOptimizer() {
		this(null, null);
	}

	public SemanticPlanOptimizer(Iterable<IPlanRewriteRule> rules, RewriteRuleCompositionOptions options) {
		if (rules == null)
			rules = List.of(new AuthorizationCanonicalizationRule(), new PredicatePushdownRule(),
					new ProjectionPruningRule(), new RelationshipTraversalOptimizationRule(),
					new RelationshipJoinOrderingRule(), new AggregateRelationshipFilterPushdownRule(),
					new AggregateCardinalityOptimizationRule());
		composer = new RewriteRuleComposer(rules, options);
	}

	@Override
	public SemanticPlanOptimizationResult optimize(SemanticPlan plan) {
		Objects.requireNonNull(plan);
		var composition = composer.compose(plan);
		return new SemanticPlanOptimizationResult(composition.plan(), composition.appliedRules(),
				SecurityPreservationProof.create(plan, composition.plan()),
				SemanticEquivalenceProof.create(plan, composition.plan()),
				SemanticPlanAuthorizationBindingProof.create(plan, composition.plan()), composition.applications(),
				composition.totalCostImpact(), composition.terminatedNormally());
	}

	public static PlanRewriteRuleResult applyRule(IPlanRewriteRule rule, SemanticPlan before, SemanticPlan after) {
		return RewriteRuleComposer.applyRule(Objects.requireNonNull(rule), Objects.requireNonNull(before),
				Objects.requireNonNull(after));
	}
}
