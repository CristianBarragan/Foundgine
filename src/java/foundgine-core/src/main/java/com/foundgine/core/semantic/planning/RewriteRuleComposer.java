package com.foundgine.core.semantic.planning;

import java.util.*;

/**
 * Deterministically composes rewrite rules while enforcing ordering, conflicts
 * and budgets.
 */
public final class RewriteRuleComposer {
	private final List<IPlanRewriteRule> orderedRules;
	private final RewriteRuleCompositionOptions options;
	private final RewriteRuleSelector selector;

	public RewriteRuleComposer(Iterable<IPlanRewriteRule> rules) {
		this(rules, null);
	}

	public RewriteRuleComposer(Iterable<IPlanRewriteRule> rules, RewriteRuleCompositionOptions options) {
		Objects.requireNonNull(rules);
		this.options = (options == null ? new RewriteRuleCompositionOptions() : options).validate();
		this.orderedRules = orderRules(toList(rules));
		this.selector = new RewriteRuleSelector(this.options.selectionPolicy(), this.options.providerCostEstimator(),
				this.options.providerCostSelectionPolicy());
	}

	public RewriteRuleCompositionResult compose(SemanticPlan plan) {
		Objects.requireNonNull(plan);
		var current = plan;
		var applications = new ArrayList<PlanRewriteRuleResult>();
		var history = new ArrayList<RewriteRuleCandidate>();
		var providerHistory = new ArrayList<ProviderAwareRewriteRuleCandidate>();
		var seen = new HashSet<String>();
		seen.add(SemanticPlanFingerprint.create(current));
		var appliedIdempotent = new HashSet<String>();
		var appliedRules = new HashSet<String>();
		for (int pass = 0;; pass++) {
			var eligible = new ArrayList<IPlanRewriteRule>();
			for (var rule : orderedRules) {
				if (rule.isIdempotent() && appliedIdempotent.contains(rule.name()))
					continue;
				if (rule.mustRunAfter().stream().anyMatch(x -> !appliedRules.contains(x)))
					continue;
				boolean blocked = false;
				for (var prerequisite : orderedRules)
					if (prerequisite.mustRunBefore().contains(rule.name())
							&& !appliedRules.contains(prerequisite.name()) && prerequisite.canApply(current)) {
						blocked = true;
						break;
					}
				if (blocked)
					continue;
				if (rule.conflictsWith().stream().anyMatch(appliedRules::contains))
					continue;
				eligible.add(rule);
			}
			String selectedName;
			if (options.providerCostEstimator() != null) {
				var selected = selector.selectProviderAware(current, eligible);
				if (selected == null)
					return result(current, applications, history, providerHistory);
				providerHistory.add(selected);
				selectedName = selected.ruleName();
			} else {
				var selected = selector.select(current, eligible);
				if (selected == null)
					return result(current, applications, history, providerHistory);
				history.add(selected);
				selectedName = selected.ruleName();
			}
			IPlanRewriteRule rule = orderedRules.stream().filter(r -> r.name().equals(selectedName)).findFirst()
					.orElseThrow();
			if (applications.size() >= options.maxRuleApplications())
				throw new IllegalStateException(
						"Rewrite rule composition exceeded the maximum rule-application budget.");
			var candidate = rule.apply(current);
			if (candidate == current) {
				appliedRules.add(rule.name());
				if (rule.isIdempotent())
					appliedIdempotent.add(rule.name());
				continue;
			}
			var result = applyRule(rule, current, candidate);
			if (!result.isSatisfied())
				throw new IllegalStateException(
						"Rewrite rule '" + rule.name() + "' did not satisfy its proof obligations.");
			var beforeFingerprint = SemanticPlanFingerprint.create(current);
			var fingerprint = SemanticPlanFingerprint.create(candidate);
			if (beforeFingerprint.equals(fingerprint)) {
				appliedRules.add(rule.name());
				if (rule.isIdempotent())
					appliedIdempotent.add(rule.name());
				continue;
			}
			if (!seen.add(fingerprint))
				throw new IllegalStateException(
						"Rewrite rule composition detected a cycle at rule '" + rule.name() + "'.");
			if (seen.size() > options.maxPlanVisits())
				throw new IllegalStateException("Rewrite rule composition exceeded the maximum plan-visit budget.");
			applications.add(result);
			current = candidate;
			appliedRules.add(rule.name());
			if (rule.isIdempotent())
				appliedIdempotent.add(rule.name());
			if (pass >= options.maxRuleApplications())
				throw new IllegalStateException("Rewrite rule composition exceeded the maximum composition passes.");
		}
	}

	private static RewriteRuleCompositionResult result(SemanticPlan p, List<PlanRewriteRuleResult> a,
			List<RewriteRuleCandidate> h, List<ProviderAwareRewriteRuleCandidate> ph) {
		return new RewriteRuleCompositionResult(p, a, a.stream().mapToDouble(PlanRewriteRuleResult::costImpact).sum(),
				true, h, ph);
	}

	private static List<IPlanRewriteRule> toList(Iterable<IPlanRewriteRule> rules) {
		var r = new ArrayList<IPlanRewriteRule>();
		for (var x : rules)
			r.add(x);
		return r;
	}

	private static List<IPlanRewriteRule> orderRules(List<IPlanRewriteRule> rules) {
		var byName = new HashMap<String, IPlanRewriteRule>();
		for (var r : rules)
			if (byName.put(r.name(), r) != null)
				throw new IllegalArgumentException("Duplicate rewrite rule '" + r.name() + "'.");
		for (var r : rules) {
			for (var dep : r.mustRunAfter())
				if (!byName.containsKey(dep))
					throw new IllegalStateException(
							"Rewrite rule '" + r.name() + "' references unknown rule '" + dep + "'.");
			for (var dep : r.mustRunBefore())
				if (!byName.containsKey(dep))
					throw new IllegalStateException(
							"Rewrite rule '" + r.name() + "' references unknown rule '" + dep + "'.");
			for (var conflict : r.conflictsWith())
				if (byName.containsKey(conflict) && byName.get(conflict).conflictsWith().contains(r.name()))
					throw new IllegalStateException(
							"Rewrite rules '" + r.name() + "' and '" + conflict + "' conflict and cannot be composed.");
		}
		var edges = new HashMap<String, Set<String>>();
		var indegree = new HashMap<String, Integer>();
		for (var r : rules) {
			edges.put(r.name(), new HashSet<>());
			indegree.put(r.name(), 0);
		}
		for (var r : rules) {
			for (var after : r.mustRunAfter())
				addEdge(edges, indegree, after, r.name());
			for (var before : r.mustRunBefore())
				addEdge(edges, indegree, r.name(), before);
		}
		var queue = new PriorityQueue<IPlanRewriteRule>(
				Comparator.comparingInt(IPlanRewriteRule::priority).thenComparing(IPlanRewriteRule::name));
		for (var r : rules)
			if (indegree.get(r.name()) == 0)
				queue.add(r);
		var ordered = new ArrayList<IPlanRewriteRule>();
		while (!queue.isEmpty()) {
			var r = queue.remove();
			ordered.add(r);
			for (var next : edges.get(r.name()))
				if (indegree.merge(next, -1, Integer::sum) == 0)
					queue.add(byName.get(next));
		}
		if (ordered.size() != rules.size())
			throw new IllegalStateException("Rewrite rule composition contains an ordering cycle.");
		return List.copyOf(ordered);
	}

	private static void addEdge(Map<String, Set<String>> edges, Map<String, Integer> indegree, String from, String to) {
		if (edges.get(from).add(to))
			indegree.merge(to, 1, Integer::sum);
	}

	public static PlanRewriteRuleResult applyRule(IPlanRewriteRule rule, SemanticPlan before, SemanticPlan after) {
		var security = SecurityPreservationProof.create(before, after);
		var obligation = SecurityObligationProof.create(rule, before, after);
		var semantic = SemanticEquivalenceProof.create(before, after);
		var authorization = SemanticPlanAuthorizationBindingProof.create(before, after);
		var optimization = PlanOptimizationProof.create(rule, before, after);
		return new PlanRewriteRuleResult(rule.name(), before, after, rule.preconditions(), rule.securityObligations(),
				rule.costImpact(), security, obligation, semantic, authorization, optimization);
	}
}
