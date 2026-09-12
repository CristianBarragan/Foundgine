package com.foundgine.core.execution.e2e;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.execution.ExecutionIRCompiler;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.planning.ExecutionOperation;
import com.foundgine.core.semantic.planning.SemanticPlan;
import com.foundgine.core.semantic.planning.SemanticPlanAuthorizationBinding;
import com.foundgine.core.semantic.planning.SemanticPlanNode;
import com.foundgine.core.execution.ExecutionIR;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

class ExecutionIRParityE2ETest {
    @Test
    void semanticPlanLowersWithoutChangingTopology() {
        var child = new SemanticPlanNode(2, ExecutionOperation.TRAVERSE,
                EntityId.create("OrderItem"), List.of(), RelationshipId.create("Order", "Items"), null, List.of());
        var root = new SemanticPlanNode(1, ExecutionOperation.SCAN,
                EntityId.create("Order"), List.of(), null, null, List.of(child));

        var plan = new SemanticPlan(root, List.of("authorization.required"),
                new SemanticPlanAuthorizationBinding("contract", "authorization"));
        var ir = ExecutionIRCompiler.compile(plan);

        assertEquals(1, ir.root().id());
        assertEquals(EntityId.create("Order"), ir.root().entityId());
        assertEquals(1, ir.root().children().size());
        assertEquals(2, ir.root().children().getFirst().id());
        assertEquals(RelationshipId.create("Order", "Items"), ir.root().children().getFirst().viaRelationship());
        assertEquals(List.of("authorization.required"), ir.requiredSecurityInvariants());
    }

    @Test
    void executionBoundaryRejectsPlansWithoutAuthorizationBinding() {
        var plan = new SemanticPlan(new SemanticPlanNode(1, ExecutionOperation.SCAN,
                EntityId.create("Customer"), List.of(), null, null, List.of()));
        assertThrows(IllegalStateException.class, () -> ExecutionIRCompiler.compile(plan));
    }
}
