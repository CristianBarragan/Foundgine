package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.query.*;
import java.util.*;

/**
 * Adds a physical short-circuit hint for COUNT predicates whose truth value
 * depends only on whether a collection is empty. The semantic filter remains
 * unchanged, so COUNT semantics, authorization, relationship visibility and
 * null/empty behavior stay defined by the semantic layer.
 */
public final class AggregateCardinalityOptimizationRule implements IPlanRewriteRule {
	@Override
	public String name() {
		return "aggregate.cardinality.short-circuit";
	}

	@Override
	public List<String> preconditions() {
		return List.of("plan contains a COUNT aggregate filter", "COUNT aggregate has no target field",
				"count comparison can be reduced to an emptiness test",
				"all eligible COUNT aggregates on the node require the same strategy");
	}

	@Override
	public List<String> securityObligations() {
		return List.of("authorization.required", "authorization.runtime", "visibility.relationship",
				"planning.cache-context-isolation");
	}

	@Override
	public double costImpact() {
		return 0.5d;
	}

	@Override
	public double benefitEstimate() {
		return 3.0d;
	}

	@Override
	public boolean isIdempotent() {
		return true;
	}

	@Override
	public int priority() {
		return 40;
	}

	@Override
	public boolean canApply(SemanticPlan plan) {
		Objects.requireNonNull(plan, "plan");
		return requiresRewrite(plan.root());
	}

	@Override
	public SemanticPlan apply(SemanticPlan plan) {
		Objects.requireNonNull(plan, "plan");
		if (!canApply(plan))
			return plan;
		boolean[] changed = { false };
		SemanticPlanNode root = rewrite(plan.root(), changed);
		return changed[0] ? new SemanticPlan(root, plan.requiredSecurityInvariants(), plan.authorizationBinding())
				: plan;
	}

	private static SemanticPlanNode rewrite(SemanticPlanNode node, boolean[] changed) {
		AggregateExecutionStrategy candidate = getStrategy(
				node.queryOptions() == null ? null : node.queryOptions().filter());
		AggregateExecutionStrategy strategy = candidate == null ? AggregateExecutionStrategy.DEFAULT : candidate;
		if (strategy != node.aggregateExecutionStrategy())
			changed[0] = true;

		List<SemanticPlanNode> children = new ArrayList<>(node.children().size());
		boolean childrenChanged = false;
		for (SemanticPlanNode child : node.children()) {
			SemanticPlanNode rewritten = rewrite(child, changed);
			children.add(rewritten);
			childrenChanged |= rewritten != child;
		}
		if (childrenChanged)
			changed[0] = true;

		if (strategy != node.aggregateExecutionStrategy() || childrenChanged) {
			return new SemanticPlanNode(node.id(), node.operation(), node.entityId(), node.fields(),
					node.viaRelationship(), node.viaConnection(), children, node.queryOptions(), node.authorization(),
					node.relationshipCardinality(), node.traversalMode(), node.traversalOrder(), strategy);
		}
		return node;
	}

	private static boolean requiresRewrite(SemanticPlanNode node) {
		AggregateExecutionStrategy desired = getStrategy(
				node.queryOptions() == null ? null : node.queryOptions().filter());
		if (desired == null)
			desired = AggregateExecutionStrategy.DEFAULT;
		return desired != node.aggregateExecutionStrategy()
				|| node.children().stream().anyMatch(AggregateCardinalityOptimizationRule::requiresRewrite);
	}

	private static AggregateExecutionStrategy getStrategy(SemanticFilterExpression filter) {
		List<SemanticAggregateFilter> aggregates = new ArrayList<>();
		collectAggregates(filter, aggregates);
		if (aggregates.isEmpty())
			return null;
		for (SemanticAggregateFilter aggregate : aggregates) {
			if (aggregate.aggregate() != SemanticFilterAggregate.COUNT || aggregate.field() != null
					|| aggregate.predicate() != null)
				return null;
		}
		AggregateExecutionStrategy result = null;
		for (SemanticAggregateFilter aggregate : aggregates) {
			AggregateExecutionStrategy strategy = AggregateExecutionStrategyResolver.resolve(aggregate.operator(),
					aggregate.value());
			if (strategy == null)
				return null;
			if (result == null)
				result = strategy;
			else if (result != strategy)
				return null;
		}
		return result;
	}

	private static void collectAggregates(SemanticFilterExpression filter, Collection<SemanticAggregateFilter> result) {
		if (filter instanceof SemanticAggregateFilter aggregate) {
			result.add(aggregate);
		} else if (filter instanceof SemanticAndFilter and) {
			and.expressions().forEach(x -> collectAggregates(x, result));
		} else if (filter instanceof SemanticOrFilter or) {
			or.expressions().forEach(x -> collectAggregates(x, result));
		}
		// Relationship predicates execute in a different semantic scope.
	}
}
