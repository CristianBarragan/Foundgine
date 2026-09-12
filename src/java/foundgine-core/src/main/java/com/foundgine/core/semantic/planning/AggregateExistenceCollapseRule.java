package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.aggregates.AggregateProviderCapability;
import com.foundgine.core.semantic.query.*;
import java.util.*;

/**
 * Collapses a bare predicate-bearing COUNT comparison into SOME/NONE when the
 * provider declares quantifier support.
 */
public final class AggregateExistenceCollapseRule implements IPlanRewriteRule {
	private final AggregateProviderCapability providerCapability;

	public AggregateExistenceCollapseRule(AggregateProviderCapability providerCapability) {
		this.providerCapability = Objects.requireNonNull(providerCapability);
	}

	@Override
	public String name() {
		return "aggregate.existence.collapse";
	}

	@Override
	public List<String> preconditions() {
		return List.of("COUNT aggregate has no target field", "COUNT aggregate has a relationship predicate",
				"COUNT comparison is provably an existence/emptiness test",
				"provider supports relationship quantifiers");
	}

	@Override
	public List<String> securityObligations() {
		return List.of("authorization.required", "authorization.runtime", "visibility.relationship",
				"planning.cache-context-isolation");
	}

	@Override
	public double costImpact() {
		return .75d;
	}

	@Override
	public double benefitEstimate() {
		return 5d;
	}

	@Override
	public int priority() {
		return 40;
	}

	@Override
	public boolean canApply(SemanticPlan plan) {
		Objects.requireNonNull(plan);
		return providerCapability.supportsRelationshipQuantifiers() && containsEligible(plan.root());
	}

	@Override
	public SemanticPlan apply(SemanticPlan plan) {
		Objects.requireNonNull(plan);
		if (!canApply(plan))
			return plan;
		var candidate = rewritePlan(plan);
		if (candidate == plan)
			return plan;
		AggregateExistenceCollapseProof.create(plan, candidate, providerCapability,
				predicateShapeMatches(plan, candidate));
		return candidate;
	}

	private static SemanticPlan rewritePlan(SemanticPlan plan) {
		boolean[] changed = { false };
		var root = rewriteNode(plan.root(), changed);
		return changed[0] ? new SemanticPlan(root, plan.requiredSecurityInvariants(), plan.authorizationBinding())
				: plan;
	}

	private static SemanticPlanNode rewriteNode(SemanticPlanNode node, boolean[] changed) {
		var options = node.queryOptions();
		if (options != null && options.filter() != null) {
			var rewritten = rewriteFilter(options.filter(), changed);
			if (rewritten != options.filter())
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
		return changed[0] && (options != node.queryOptions() || childChanged) ? new SemanticPlanNode(node.id(),
				node.operation(), node.entityId(), node.fields(), node.viaRelationship(), node.viaConnection(),
				children, options, node.authorization(), node.relationshipCardinality(), node.traversalMode(),
				node.traversalOrder(), node.aggregateExecutionStrategy()) : node;
	}

	private static SemanticFilterExpression rewriteFilter(SemanticFilterExpression filter, boolean[] changed) {
		if (filter instanceof SemanticAggregateFilter a && isEligible(a)) {
			changed[0] = true;
			var q = isEmptyStrategy(a) ? SemanticRelationshipQuantifier.NONE : SemanticRelationshipQuantifier.SOME;
			return new SemanticRelationshipFilter(a.relationship(), q, a.predicate());
		}
		if (filter instanceof SemanticAndFilter a) {
			var out = new ArrayList<SemanticFilterExpression>();
			boolean c = false;
			for (var e : a.expressions()) {
				var r = rewriteFilter(e, changed);
				out.add(r);
				c |= r != e;
			}
			return c ? new SemanticAndFilter(out) : filter;
		}
		if (filter instanceof SemanticOrFilter o) {
			var out = new ArrayList<SemanticFilterExpression>();
			boolean c = false;
			for (var e : o.expressions()) {
				var r = rewriteFilter(e, changed);
				out.add(r);
				c |= r != e;
			}
			return c ? new SemanticOrFilter(out) : filter;
		}
		if (filter instanceof SemanticRelationshipFilter r) {
			var p = rewriteFilter(r.predicate(), changed);
			return p == r.predicate() ? r : new SemanticRelationshipFilter(r.relationship(), r.quantifier(), p);
		}
		return filter;
	}

	private static boolean containsEligible(SemanticPlanNode node) {
		return (node.queryOptions() != null && node.queryOptions().filter() != null
				&& containsEligible(node.queryOptions().filter()))
				|| node.children().stream().anyMatch(AggregateExistenceCollapseRule::containsEligible);
	}

	private static boolean containsEligible(SemanticFilterExpression filter) {
		if (filter instanceof SemanticAggregateFilter a)
			return isEligible(a);
		if (filter instanceof SemanticAndFilter a)
			return a.expressions().stream().anyMatch(AggregateExistenceCollapseRule::containsEligible);
		if (filter instanceof SemanticOrFilter o)
			return o.expressions().stream().anyMatch(AggregateExistenceCollapseRule::containsEligible);
		if (filter instanceof SemanticRelationshipFilter r)
			return containsEligible(r.predicate());
		return false;
	}

	private static boolean isEligible(SemanticAggregateFilter a) {
		if (a.aggregate() != SemanticFilterAggregate.COUNT || a.field() != null || a.predicate() == null)
			return false;
		var s = AggregateExecutionStrategyResolver.resolve(a.operator(), a.value());
		return s == AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT
				|| s == AggregateExecutionStrategy.COUNT_EMPTY_SHORT_CIRCUIT;
	}

	private static boolean isEmptyStrategy(SemanticAggregateFilter a) {
		return AggregateExecutionStrategyResolver.resolve(a.operator(),
				a.value()) == AggregateExecutionStrategy.COUNT_EMPTY_SHORT_CIRCUIT;
	}

	private static boolean predicateShapeMatches(SemanticPlan before, SemanticPlan after) {
		var b = collect(before.root()).stream().sorted().toList();
		var a = collect(after.root()).stream().sorted().toList();
		return b.equals(a);
	}

	private static List<String> collect(SemanticPlanNode node) {
		var r = new ArrayList<String>();
		if (node.queryOptions() != null && node.queryOptions().filter() != null)
			collect(node.queryOptions().filter(), r);
		node.children().forEach(c -> r.addAll(collect(c)));
		return r;
	}

	private static void collect(SemanticFilterExpression f, List<String> out) {
		if (f instanceof SemanticAggregateFilter a && isEligible(a))
			out.add(a.relationship().value() + "|" + a.predicate());
		else if (f instanceof SemanticRelationshipFilter r)
			out.add(r.relationship().value() + "|" + r.predicate());
		else if (f instanceof SemanticAndFilter a)
			a.expressions().forEach(x -> collect(x, out));
		else if (f instanceof SemanticOrFilter o)
			o.expressions().forEach(x -> collect(x, out));
	}
}
