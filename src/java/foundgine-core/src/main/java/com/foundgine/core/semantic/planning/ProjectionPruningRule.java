package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.FieldId;
import java.util.*;

/** Removes redundant duplicate projection fields while preserving first occurrence order. */
public final class ProjectionPruningRule implements IPlanRewriteRule {
    @Override public String name() { return "projection.pruning"; }
    @Override public List<String> preconditions() { return List.of("plan contains a projection", "projection contains redundant fields"); }
    @Override public List<String> securityObligations() { return List.of("visibility.field", "visibility.relationship", "authorization.required"); }
    @Override public double costImpact() { return .25d; }
    @Override public double benefitEstimate() { return 1.5d; }
    @Override public int priority() { return 20; }
    @Override public boolean canApply(SemanticPlan plan) { Objects.requireNonNull(plan); return containsRedundantProjection(plan.root()); }
    @Override public SemanticPlan apply(SemanticPlan plan) {
        Objects.requireNonNull(plan);
        if (!canApply(plan)) return plan;
        boolean[] changed = {false};
        var root = rewriteNode(plan.root(), changed);
        return changed[0] ? new SemanticPlan(root, plan.requiredSecurityInvariants(), plan.authorizationBinding()) : plan;
    }
    private static SemanticPlanNode rewriteNode(SemanticPlanNode node, boolean[] changed) {
        List<FieldId> fields = dedupe(node.fields(), changed);
        List<SemanticPlanNode> children = new ArrayList<>(node.children().size());
        boolean childChanged = false;
        for (var child : node.children()) { var r = rewriteNode(child, changed); children.add(r); childChanged |= r != child; }
        if (childChanged) changed[0] = true;
        if (fields != node.fields() || childChanged)
            return new SemanticPlanNode(node.id(), node.operation(), node.entityId(), fields, node.viaRelationship(), node.viaConnection(), children,
                    node.queryOptions(), node.authorization(), node.relationshipCardinality(), node.traversalMode(), node.traversalOrder(), node.aggregateExecutionStrategy());
        return node;
    }
    private static List<FieldId> dedupe(List<FieldId> fields, boolean[] changed) {
        if (fields.size() < 2) return fields;
        Set<FieldId> seen = new HashSet<>(); List<FieldId> result = new ArrayList<>(fields.size());
        for (var field : fields) { if (seen.add(field)) result.add(field); else changed[0] = true; }
        return result.size() == fields.size() ? fields : result;
    }
    private static boolean containsRedundantProjection(SemanticPlanNode node) {
        return hasDuplicates(node.fields()) || node.children().stream().anyMatch(ProjectionPruningRule::containsRedundantProjection);
    }
    private static boolean hasDuplicates(List<FieldId> fields) { Set<FieldId> seen = new HashSet<>(); for (var f : fields) if (!seen.add(f)) return true; return false; }
}
