package com.foundgine.core.execution;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationEvidence;
import com.foundgine.core.semantic.planning.ExecutionOperation;
import com.foundgine.core.semantic.planning.SemanticPlan;
import com.foundgine.core.semantic.planning.SemanticPlanAuthorizationBinding;
import com.foundgine.core.semantic.planning.SemanticPlanNode;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

class ExecutionIRBoundaryTests {
	@Test
	void executionIrRequiresAuthorizationBinding() {
		var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, EntityId.create("Customer"), List.of(), null, null,
				List.of());
		var plan = new SemanticPlan(node);
		assertThrows(IllegalStateException.class, () -> ExecutionIRCompiler.compile(plan));
	}

	@Test
	void executionIrCopiesPlanTopologyAndSecurityRequirements() {
		var entity = EntityId.create("Customer");
		var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, entity, List.of(), null, null, List.of());
		var binding = new SemanticPlanAuthorizationBinding("contract", "authorization");
		var plan = new SemanticPlan(node, List.of("authorization.required", "visibility.field"), binding);

		var ir = ExecutionIRCompiler.compile(plan);
		assertEquals(1, ir.root().id());
		assertEquals(entity, ir.root().entityId());
		assertEquals(List.of("authorization.required", "visibility.field"), ir.requiredSecurityInvariants());
		assertSame(binding, ir.authorizationBinding());
	}
}
