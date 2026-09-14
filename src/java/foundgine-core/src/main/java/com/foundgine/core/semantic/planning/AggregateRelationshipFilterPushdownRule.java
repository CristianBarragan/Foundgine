package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.query.*;
import java.util.*;

/**
 * Pushes an existential relationship predicate into a COUNT aggregate. COUNT(R)
 * > 0 AND SOME(R, P) becomes COUNT(R WHERE P) > 0.
 */
public final class AggregateRelationshipFilterPushdownRule implements IPlanRewriteRule {
	@Override
	public String name() {
		return "aggregate.relationship-filter.pushdown";
	}

	@Override
	public List<String> preconditions() {
		return List.of("plan contains an AND filter", "AND contains an eligible COUNT existence predicate",
				"AND contains a SOME relationship predicate for the same relationship",
				"COUNT aggregate does not already contain a predicate");
	}

	@Override
	public List<String> securityObligations() {
		return List.of("authorization.required", "authorization.runtime", "visibility.relationship",
				"planning.cache-context-isolation");
	}

	@Override
	public double costImpact() {
		return 1.25d;
	}

	@Override
	public double benefitEstimate() {
		return 4d;
	}

	@Override
	public List<String> mustRunBefore() {
		return List.of("aggregate.cardinality.short-circuit");
	}

	@Override
	public int priority() {
		return 35;
	}

	@Override
	public boolean canApply(SemanticPlan plan) {
		Objects.requireNonNull(plan);
		return containsEligible(plan.root());
	}

	@Override
	public SemanticPlan apply(SemanticPlan plan) {
		Objects.requireNonNull(plan);
		if (!canApply(plan))
			return plan;
		boolean[] changed = { false };
		var root = rewriteNode(plan.root(), changed);
		return changed[0] ? new SemanticPlan(root, plan.requiredSecurityInvariants(), plan.authorizationBinding())
				: plan;
	}

	private static SemanticPlanNode rewriteNode(SemanticPlanNode node, boolean[] changed) {
		var options = node.queryOptions();
		boolean filterChanged = false;
		if (options != null && options.filter() != null) {
			var original = options.filter();
			var rewritten = rewriteFilter(original, changed);
			filterChanged = rewritten != original;
			if (filterChanged)
				options = new SemanticQueryOptions(rewritten, options.order(), options.limit(), options.offset(),
						options.after());
		}
		List<SemanticPlanNode> children = new ArrayList<>(node.children().size());
		boolean childChanged = false;
		for (var child : node.children()) {
			var r = rewriteNode(child, changed);
			children.add(r);
			childChanged |= r != child;
		}
		if (childChanged)
			changed[0] = true;
		if (filterChanged)
			changed[0] = true;
		if (filterChanged || childChanged)
			return new SemanticPlanNode(node.id(), node.operation(), node.entityId(), node.fields(),
					node.viaRelationship(), node.viaConnection(), children, options, node.authorization(),
					node.relationshipCardinality(), node.traversalMode(), node.traversalOrder(),
					filterChanged ? AggregateExecutionStrategy.DEFAULT : node.aggregateExecutionStrategy());
		return node;
	}

	private static SemanticFilterExpression rewriteFilter(SemanticFilterExpression filter, boolean[] changed) {
		if (filter instanceof SemanticAndFilter and) {
			var expressions = new ArrayList<>(and.expressions());
			for (int i = 0; i < expressions.size(); i++) {
				var aggregate = expressions.get(i);
				if (!(aggregate instanceof SemanticAggregateFilter a) || !isEligibleCountExists(a))
					continue;
				for (int j = 0; j < expressions.size(); j++) {
					if (i == j || !(expressions.get(j) instanceof SemanticRelationshipFilter r)
							|| r.quantifier() != SemanticRelationshipQuantifier.SOME
							|| !r.relationship().equals(a.relationship()))
						continue;
					var rewritten = new SemanticAggregateFilter(a.relationship(), a.aggregate(), a.field(),
							a.operator(), a.value(), r.predicate());
					var remaining = new ArrayList<SemanticFilterExpression>();
					for (int k = 0; k < expressions.size(); k++) {
						if (k == i)
							remaining.add(rewritten);
						else if (k != j)
							remaining.add(expressions.get(k));
					}
					changed[0] = true;
					if (remaining.isEmpty())
						throw new IllegalStateException("Aggregate pushdown produced an empty AND expression.");
					return remaining.size() == 1 ? remaining.get(0) : new SemanticAndFilter(remaining);
				}
			}
			var nested = new ArrayList<SemanticFilterExpression>(expressions.size());
			boolean nestedChanged = false;
			for (var expression : expressions) {
				var r = rewriteFilter(expression, changed);
				nested.add(r);
				nestedChanged |= r != expression;
			}
			return nestedChanged ? new SemanticAndFilter(nested) : filter;
		}
		if (filter instanceof SemanticOrFilter or) {
			var nested = new ArrayList<SemanticFilterExpression>(or.expressions().size());
			boolean nestedChanged = false;
			for (var expression : or.expressions()) {
				var r = rewriteFilter(expression, changed);
				nested.add(r);
				nestedChanged |= r != expression;
			}
			return nestedChanged ? new SemanticOrFilter(nested) : filter;
		}
		if (filter instanceof SemanticRelationshipFilter relationship) {
			var predicate = rewriteFilter(relationship.predicate(), changed);
			return predicate == relationship.predicate() ? relationship
					: new SemanticRelationshipFilter(relationship.relationship(), relationship.quantifier(), predicate);
		}
		return filter;
	}

	private static boolean containsEligible(SemanticPlanNode node) {
		return (node.queryOptions() != null && node.queryOptions().filter() != null
				&& containsEligible(node.queryOptions().filter()))
				|| node.children().stream().anyMatch(AggregateRelationshipFilterPushdownRule::containsEligible);
	}

	private static boolean containsEligible(SemanticFilterExpression filter) {
		if (filter instanceof SemanticAndFilter and)
			return and.expressions().stream()
					.anyMatch(x -> x instanceof SemanticAggregateFilter a && isEligibleCountExists(a))
					&& and.expressions().stream().anyMatch(x -> x instanceof SemanticRelationshipFilter r
							&& r.quantifier() == SemanticRelationshipQuantifier.SOME);
		if (filter instanceof SemanticRelationshipFilter r)
			return containsEligible(r.predicate());
		if (filter instanceof SemanticOrFilter or)
			return or.expressions().stream().anyMatch(AggregateRelationshipFilterPushdownRule::containsEligible);
		return false;
	}

	private static boolean isEligibleCountExists(SemanticAggregateFilter a) {
		return a.aggregate() == SemanticFilterAggregate.COUNT && a.field() == null && a.predicate() == null
				&& AggregateExecutionStrategyResolver.resolve(a.operator(),
						a.value()) == AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT;
	}
}
