package com.foundgine.providers.storage.orm;

import com.foundgine.core.execution.ExecutionIR;
import com.foundgine.core.execution.ExecutionIRBoundary;
import com.foundgine.core.execution.ExecutionIRNode;
import com.foundgine.core.execution.IProviderPlanCompiler;
import com.foundgine.core.execution.ISecurityInvariantProviderCompiler;
import com.foundgine.core.execution.ProviderPlan;
import com.foundgine.core.execution.security.IProviderSecurityConformanceEvaluator;
import com.foundgine.core.execution.security.ProviderSecurityConformanceResult;
import com.foundgine.core.semantic.security.SecurityInvariantIds;

import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.Objects;

/** Validates ExecutionIR against the registry (capability set) and binds authorization. */
public class OrmPlanCompiler
        implements IProviderPlanCompiler,
                ISecurityInvariantProviderCompiler,
                IProviderSecurityConformanceEvaluator {
    private final String provider;
    private final OrmRegistry registry;

    public OrmPlanCompiler(String provider, OrmRegistry registry) {
        this.provider = Objects.requireNonNull(provider, "provider");
        this.registry = Objects.requireNonNull(registry, "registry");
    }

    @Override
    public Collection<String> preservedSecurityInvariants() {
        return List.of(
                SecurityInvariantIds.AUTHORIZATION_REQUIRED,
                SecurityInvariantIds.RUNTIME_AUTHORIZATION,
                SecurityInvariantIds.FIELD_VISIBILITY,
                SecurityInvariantIds.RELATIONSHIP_VISIBILITY,
                SecurityInvariantIds.PARAMETERIZED_VALUES,
                SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION);
    }

    @Override
    public ProviderPlan compile(ExecutionIR ir) {
        Objects.requireNonNull(ir, "ir");
        validateTree(ir.root(), null);
        var plan = new OrmPlan(provider, ir);
        ExecutionIRBoundary.bindProviderPlan(ir, plan);
        return plan;
    }

    private void validateTree(ExecutionIRNode node, ExecutionIRNode parent) {
        OrmQueries.validate(registry, registry.map(node.entityId()), node, parent == null);
        if (parent != null) {
            registry.link(node.viaRelationship(), parent.entityId(), node.entityId());
        }
        for (var child : node.children()) validateTree(child, node);
    }

    @Override
    public ProviderSecurityConformanceResult evaluate(ExecutionIR ir, ProviderPlan plan) {
        Objects.requireNonNull(ir, "ir");
        Objects.requireNonNull(plan, "plan");
        if (!(plan instanceof OrmPlan ormPlan)) {
            throw new IllegalArgumentException("Expected an OrmPlan.");
        }
        var required = ir.requiredSecurityInvariants().stream().distinct().sorted().toList();
        var preserved = preservedSecurityInvariants();
        var satisfied = required.stream().filter(preserved::contains).toList();
        var violations = new ArrayList<String>();
        required.stream()
                .filter(x -> !satisfied.contains(x))
                .forEach(
                        x ->
                                violations.add(
                                        "Invariant '"
                                                + x
                                                + "' is not certified by the "
                                                + provider
                                                + " execution plan."));
        if (ormPlan.ir() != ir) {
            violations.add("The " + provider + " plan does not reference the supplied ExecutionIR.");
        }
        if (ormPlan.authorizationBinding() == null) {
            violations.add("The " + provider + " plan carries no authorization provenance.");
        }
        return new ProviderSecurityConformanceResult(
                ormPlan.provider(), required, satisfied, List.copyOf(violations));
    }
}
