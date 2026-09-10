package com.foundgine.core.execution;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.ConnectionId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationEvidence;
import com.foundgine.core.semantic.planning.AggregateExecutionStrategy;
import com.foundgine.core.semantic.planning.ExecutionOperation;
import com.foundgine.core.semantic.planning.SemanticPlan;
import com.foundgine.core.semantic.planning.SemanticPlanAuthorizationBinding;
import com.foundgine.core.semantic.planning.SemanticPlanNode;
import com.foundgine.core.semantic.query.SemanticQueryOptions;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.List;
import java.util.Objects;

/**
 * Canonical provider-neutral execution representation.
 *
 * <p>Semantic IR answers what the operation means. Execution IR answers what
 * provider-neutral work must be performed. It deliberately contains no SQL,
 * storage names, provider types, aliases, or connection details.</p>
 */
public record ExecutionIR(
        ExecutionIRNode root,
        List<String> requiredSecurityInvariants,
        SemanticPlanAuthorizationBinding authorizationBinding) {

    public ExecutionIR {
        Objects.requireNonNull(root, "root");
        requiredSecurityInvariants = requiredSecurityInvariants == null
                ? List.of()
                : List.copyOf(requiredSecurityInvariants);
        Objects.requireNonNull(authorizationBinding, "authorizationBinding");
    }

    public static ExecutionIR from(SemanticPlan plan) {
        Objects.requireNonNull(plan, "plan");
        if (plan.authorizationBinding() == null) {
            throw new IllegalStateException("An executable plan must carry authorization provenance.");
        }
        return new ExecutionIR(
                ExecutionIRNode.from(plan.root()),
                plan.effectiveSecurityInvariants(),
                plan.authorizationBinding());
    }
}

