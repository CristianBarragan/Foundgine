package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.query.*;
import java.util.*;

/** Assigns a deterministic physical traversal order to eligible sibling relationship nodes. */
public final class RelationshipJoinOrderingRule implements IPlanRewriteRule {
    @Override public String name() { return "relationship.join.order"; }
    @Override public List<String> preconditions() { return List.of("plan contains two or more sibling relationship traversals", "relationship cardinality is known for each candidate traversal", "no explicit sibling execution order is already fixed"); }
    @Override public List<String> securityObligations() { return List.of("authorization.required", "authorization.runtime", "visibility.relationship", "planning.cache-context-isolation"); }
    @Override public double costImpact() { return 1d; }
    @Override public double benefitEstimate() { return 2.5d; }
    @Override public int priority() { return 30; }
    @Override public boolean canApply(SemanticPlan plan) { Objects.requireNonNull(plan); return containsEligibleSiblingSet(plan.root()); }
    @Override public SemanticPlan apply(SemanticPlan plan) {
        Objects.requireNonNull(plan); if (!canApply(plan)) return plan;
        boolean[] changed = {false}; var root = rewrite(plan.root(), changed);
        return changed[0] ? new SemanticPlan(root, plan.requiredSecurityInvariants(), plan.authorizationBinding()) : plan;
    }
    private static SemanticPlanNode rewrite(SemanticPlanNode node, boolean[] changed) {
        List<SemanticPlanNode> children = new ArrayList<>(node.children());
        var candidates = new ArrayList<Integer>();
        for (int i = 0; i < children.size(); i++) {
            var child = children.get(i);
            if (child.viaRelationship() != null && child.relationshipCardinality() != null && child.traversalOrder() < 0) candidates.add(i);
        }
        if (candidates.size() >= 2) {
            candidates.sort(Comparator.comparingInt((Integer i) -> selectivityClass(children.get(i)))
                    .thenComparingInt(i -> children.get(i).relationshipCardinality() == RelationshipCardinality.ONE ? 0 : 1)
                    .thenComparingLong(i -> children.get(i).viaRelationship().value()).thenComparingInt(Integer::intValue));
            for (int rank = 0; rank < candidates.size(); rank++) {
                int index = candidates.get(rank); var child = children.get(index);
                if (child.traversalOrder() != rank) {
                    children.set(index, copyWithTraversalOrder(child, rank)); changed[0] = true;
                }
            }
        }
        boolean childChanged = false;
        for (int i = 0; i < children.size(); i++) { var before = children.get(i); var after = rewrite(before, changed); if (after != before) { children.set(i, after); childChanged = true; } }
        if (childChanged) changed[0] = true;
        return childChanged || !children.equals(node.children()) ? copyWithChildren(node, children) : node;
    }
    private static int selectivityClass(SemanticPlanNode node) {
        int score = 0;
        var filter = node.queryOptions() == null ? null : node.queryOptions().filter();
        if (filter != null) score -= 3;
        if (filter instanceof SemanticRelationshipFilter || filter instanceof SemanticAggregateFilter) score -= 1;
        if (node.queryOptions() != null && node.queryOptions().limit() != null && node.queryOptions().limit() > 0) score -= 2;
        if (node.relationshipCardinality() == RelationshipCardinality.ONE) score -= 1;
        return score;
    }
    private static boolean containsEligibleSiblingSet(SemanticPlanNode node) {
        long count = node.children().stream().filter(c -> c.viaRelationship() != null && c.relationshipCardinality() != null && c.traversalOrder() < 0).count();
        return count >= 2 || node.children().stream().anyMatch(RelationshipJoinOrderingRule::containsEligibleSiblingSet);
    }
    private static SemanticPlanNode copyWithTraversalOrder(SemanticPlanNode n, int order) { return new SemanticPlanNode(n.id(),n.operation(),n.entityId(),n.fields(),n.viaRelationship(),n.viaConnection(),n.children(),n.queryOptions(),n.authorization(),n.relationshipCardinality(),n.traversalMode(),order,n.aggregateExecutionStrategy()); }
    private static SemanticPlanNode copyWithChildren(SemanticPlanNode n,List<SemanticPlanNode> children) { return new SemanticPlanNode(n.id(),n.operation(),n.entityId(),n.fields(),n.viaRelationship(),n.viaConnection(),children,n.queryOptions(),n.authorization(),n.relationshipCardinality(),n.traversalMode(),n.traversalOrder(),n.aggregateExecutionStrategy()); }
}
