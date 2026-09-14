package com.foundgine.core.execution;

import com.foundgine.core.semantic.planning.SemanticPlan;
import com.foundgine.core.semantic.planning.SemanticPlanAuthorizationBinding;

import java.util.List;
import java.util.Objects;

/**
 * Canonical provider-neutral execution representation.
 *
 * <p>Semantic IR answers what the operation means. Execution IR answers what provider-neutral work
 * must be performed. It deliberately contains no SQL, storage names, provider types, aliases, or
 * connection details.
 */
public record ExecutionIR(
        ExecutionIRNode root,
        List<String> requiredSecurityInvariants,
        SemanticPlanAuthorizationBinding authorizationBinding) {

    public ExecutionIR {
        Objects.requireNonNull(root, "root");
        requiredSecurityInvariants =
                requiredSecurityInvariants == null
                        ? List.of()
                        : List.copyOf(requiredSecurityInvariants);
        Objects.requireNonNull(authorizationBinding, "authorizationBinding");
    }

    public static ExecutionIR from(SemanticPlan plan) {
        Objects.requireNonNull(plan, "plan");
        if (plan.authorizationBinding() == null) {
            throw new IllegalStateException(
                    "An executable plan must carry authorization provenance.");
        }
        return new ExecutionIR(
                ExecutionIRNode.from(plan.root()),
                plan.effectiveSecurityInvariants(),
                plan.authorizationBinding());
    }
}
