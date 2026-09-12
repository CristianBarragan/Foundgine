package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of {@code SemanticPlanAuthorizationBindingProofTests}
 * (Foundgine.Planning.Tests).
 */
class SemanticPlanAuthorizationBindingProofParityTest {

	@Test
	void rewriteMustPreserveAuthorizationBinding() {
		var before = plan(new SemanticPlanAuthorizationBinding("contract-a", "authorization-a"));
		var after = plan(null);

		assertThrows(IllegalStateException.class, () -> SemanticPlanAuthorizationBindingProof.create(before, after));
	}

	@Test
	void rewriteCannotReplaceAuthorizationBinding() {
		var before = plan(new SemanticPlanAuthorizationBinding("contract-a", "authorization-a"));
		var after = plan(new SemanticPlanAuthorizationBinding("contract-b", "authorization-b"));

		assertThrows(IllegalStateException.class, () -> SemanticPlanAuthorizationBindingProof.create(before, after));
	}

	@Test
	void unboundPlansRemainUnboundDuringOptimization() {
		var source = plan(null);
		var result = new SemanticPlanOptimizer().optimize(source);

		assertTrue(result.authorizationBindingProof().isSatisfied());
		assertNull(result.plan().authorizationBinding());
	}

	private static SemanticPlan plan(SemanticPlanAuthorizationBinding binding) {
		var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1), List.of(new FieldId(1)), null,
				null, List.of());
		return new SemanticPlan(node, List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED), binding);
	}
}
