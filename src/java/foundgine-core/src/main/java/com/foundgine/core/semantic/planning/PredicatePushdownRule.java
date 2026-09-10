package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.query.*;
import java.util.*;

/**
 * Pushes a conjunctive predicate into an OR predicate using the distributive
 * law: (A OR B) AND C becomes (A AND C) OR (B AND C).
 *
 * <p>This is provider-neutral logical pushdown. It does not move predicates
 * across relationship boundaries, authorization boundaries, pagination, or
 * cardinality-changing operations.
 */
public final class PredicatePushdownRule implements IPlanRewriteRule {
    private static final int MAX_EXPANSION_TERMS = 16;
    @Override public String name() { return "predicate.pushdown.disjunction"; }
    @Override public List<String> preconditions() { return List.of("plan contains a query filter", "filter contains AND with OR operand", "rewrite expansion remains bounded"); }
    @Override public List<String> securityObligations() { return List.of("authorization.required", "visibility.field", "visibility.relationship"); }
    @Override public double costImpact() { return 1d; }
    @Override public double benefitEstimate() { return 2d; }
    @Override public int priority() { return 10; }

    @Override public boolean canApply(SemanticPlan plan) { Objects.requireNonNull(plan); return containsPushableFilter(plan.root()); }

    @Override public SemanticPlan apply(SemanticPlan plan) {
        Objects.requireNonNull(plan);
        if (!canApply(plan)) return plan;
        boolean[] changed = {false};
        SemanticPlanNode root = rewriteNode(plan.root(), changed);
        return changed[0] ? new SemanticPlan(root, plan.requiredSecurityInvariants(), plan.authorizationBinding()) : plan;
    }

    private static SemanticPlanNode rewriteNode(SemanticPlanNode node, boolean[] changed) {
        var options = node.queryOptions();
        if (options != null && options.filter() != null) {
            var rewritten = pushOnce(options.filter());
            if (rewritten != options.filter()) {
                options = new SemanticQueryOptions(rewritten, options.order(), options.limit(), options.offset(), options.after());
                changed[0] = true;
            }
        }
        List<SemanticPlanNode> children = new ArrayList<>(node.children().size());
        boolean childChanged = false;
        for (var child : node.children()) {
            var rewritten = rewriteNode(child, changed);
            children.add(rewritten); childChanged |= rewritten != child;
        }
        if (childChanged) changed[0] = true;
        return (options != node.queryOptions() || childChanged)
                ? new SemanticPlanNode(node.id(), node.operation(), node.entityId(), node.fields(), node.viaRelationship(), node.viaConnection(), children,
                    options, node.authorization(), node.relationshipCardinality(), node.traversalMode(), node.traversalOrder(), node.aggregateExecutionStrategy())
                : node;
    }

    private static SemanticFilterExpression pushOnce(SemanticFilterExpression filter) {
        if (filter instanceof SemanticAndFilter and) {
            List<SemanticFilterExpression> expressions = new ArrayList<>(and.expressions());
            int orIndex = -1;
            for (int i = 0; i < expressions.size(); i++) if (expressions.get(i) instanceof SemanticOrFilter) { orIndex = i; break; }
            if (orIndex < 0) return filter;
            var or = (SemanticOrFilter) expressions.get(orIndex);
            var other = new ArrayList<>(expressions); other.remove(orIndex);
            if (or.expressions().isEmpty() || or.expressions().size() * Math.max(1, other.size()) > MAX_EXPANSION_TERMS) return filter;
            List<SemanticFilterExpression> distributed = new ArrayList<>(or.expressions().size());
            for (var branch : or.expressions()) {
                List<SemanticFilterExpression> terms = new ArrayList<>(); terms.add(branch); terms.addAll(other);
                distributed.add(terms.size() == 1 ? terms.get(0) : new SemanticAndFilter(terms));
            }
            return new SemanticOrFilter(distributed);
        }
        if (filter instanceof SemanticOrFilter or)
            return new SemanticOrFilter(or.expressions().stream().map(PredicatePushdownRule::pushOnce).toList());
        if (filter instanceof SemanticRelationshipFilter relationship) {
            var predicate = pushOnce(relationship.predicate());
            return predicate == relationship.predicate() ? relationship : new SemanticRelationshipFilter(relationship.relationship(), relationship.quantifier(), predicate);
        }
        return filter;
    }

    private static boolean containsPushableFilter(SemanticPlanNode node) {
        return (node.queryOptions() != null && node.queryOptions().filter() != null && containsPushable(node.queryOptions().filter()))
                || node.children().stream().anyMatch(PredicatePushdownRule::containsPushableFilter);
    }

    private static boolean containsPushable(SemanticFilterExpression filter) {
        if (filter instanceof SemanticAndFilter and)
            return and.expressions().stream().anyMatch(x -> x instanceof SemanticOrFilter || containsPushable(x));
        if (filter instanceof SemanticOrFilter or) return or.expressions().stream().anyMatch(PredicatePushdownRule::containsPushable);
        if (filter instanceof SemanticRelationshipFilter relationship) return containsPushable(relationship.predicate());
        return false;
    }
}
