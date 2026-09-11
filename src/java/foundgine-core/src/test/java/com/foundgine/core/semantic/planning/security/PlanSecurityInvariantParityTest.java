package com.foundgine.core.semantic.planning.security;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.authorization.AuthorizationPredicate;
import com.foundgine.core.semantic.authorization.AuthorizationPredicateKind;
import com.foundgine.core.semantic.planning.*;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class PlanSecurityInvariantParityTest {
    @Test
    void requirementsAreDerivedFromAuthorizedShape() {
        var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
                java.util.List.of(new FieldId(2)), null, null, java.util.List.of());
        var plan = SecurityInvariantPlanRequirements.attach(new SemanticPlan(node));
        assertTrue(plan.requiredSecurityInvariants().contains(SecurityInvariantIds.AUTHORIZATION_REQUIRED));
        assertTrue(plan.requiredSecurityInvariants().contains(SecurityInvariantIds.PARAMETERIZED_VALUES));
        assertTrue(plan.requiredSecurityInvariants().contains(SecurityInvariantIds.PLAN_CACHE_CONTEXT_ISOLATION));
        assertTrue(plan.requiredSecurityInvariants().contains(SecurityInvariantIds.FIELD_VISIBILITY));
    }

    @Test
    void authorizationPredicateRequiresRuntimeAuthorization() {
        var predicate = new AuthorizationPredicate(
                AuthorizationPredicateKind.EQUAL,
                new AuthorizationPredicate(AuthorizationPredicateKind.CONSTANT, "1"),
                new AuthorizationPredicate(AuthorizationPredicateKind.CONSTANT, "1"));
        var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
                java.util.List.of(new FieldId(2)), null, null, java.util.List.of(), null, predicate);
        var plan = SecurityInvariantPlanRequirements.attach(new SemanticPlan(node));
        assertTrue(plan.requiredSecurityInvariants().contains(SecurityInvariantIds.RUNTIME_AUTHORIZATION));
    }

    @Test
    void unknownCapabilityInvariantFailsClosed() {
        var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
                java.util.List.of(), null, null, java.util.List.of());
        assertThrows(IllegalStateException.class, () ->
                SecurityInvariantPlanRequirements.attach(new SemanticPlan(node), java.util.List.of("not-a-real-invariant")));
    }
}
