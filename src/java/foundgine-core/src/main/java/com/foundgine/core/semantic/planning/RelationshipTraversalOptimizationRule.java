package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.RelationshipCardinality;
import java.util.*;

/** Adds a cardinality-aware traversal hint to relationship nodes. */
public final class RelationshipTraversalOptimizationRule implements IPlanRewriteRule {
    @Override public String name() { return "relationship.traversal.strategy"; }
    @Override public List<String> preconditions() { return List.of("plan contains relationship traversal nodes", "relationship cardinality metadata is available"); }
    @Override public List<String> securityObligations() { return List.of("authorization.required", "visibility.relationship", "planning.cache-context-isolation"); }
    @Override public double costImpact() { return .5d; }
    @Override public double benefitEstimate() { return 1.5d; }
    @Override public int priority() { return 20; }
    @Override public boolean canApply(SemanticPlan plan) { Objects.requireNonNull(plan); return containsEligible(plan.root()); }
    @Override public SemanticPlan apply(SemanticPlan plan) {
        Objects.requireNonNull(plan); if (!canApply(plan)) return plan;
        boolean[] changed = {false}; var root = rewrite(plan.root(), changed);
        return changed[0] ? new SemanticPlan(root, plan.requiredSecurityInvariants(), plan.authorizationBinding()) : plan;
    }
    private static SemanticPlanNode rewrite(SemanticPlanNode node, boolean[] changed) {
        var mode = node.traversalMode();
        if (node.viaRelationship() != null && node.relationshipCardinality() != null) {
            var target = node.relationshipCardinality() == RelationshipCardinality.ONE ? RelationshipTraversalMode.SINGLE_HOP : RelationshipTraversalMode.SET_BASED;
            if (mode != target) { mode = target; changed[0] = true; }
        }
        List<SemanticPlanNode> children = new ArrayList<>(node.children().size()); boolean childChanged = false;
        for (var child : node.children()) { var r = rewrite(child, changed); children.add(r); childChanged |= r != child; }
        if (childChanged) changed[0] = true;
        if (mode != node.traversalMode() || childChanged)
            return new SemanticPlanNode(node.id(), node.operation(), node.entityId(), node.fields(), node.viaRelationship(), node.viaConnection(), children, node.queryOptions(), node.authorization(), node.relationshipCardinality(), mode, node.traversalOrder(), node.aggregateExecutionStrategy());
        return node;
    }
    private static boolean containsEligible(SemanticPlanNode node) {
        return (node.viaRelationship() != null && node.relationshipCardinality() != null) || node.children().stream().anyMatch(RelationshipTraversalOptimizationRule::containsEligible);
    }
}
