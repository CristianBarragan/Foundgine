package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import org.junit.jupiter.api.Test;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

class AuthorizationPreservationParityTest {
	private static SemanticPlan plan(List<String> invariants) {
		return new SemanticPlan(
				new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1), List.of(), null, null, List.of()),
				invariants, null);
	}

	@Test
	void identicalSecurityContractIsPreserved() {
		var source = plan(List.of(SecurityInvariantIds.TENANT_ISOLATION, SecurityInvariantIds.AUTHORIZATION_REQUIRED));
		var rewritten = plan(
				List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED, SecurityInvariantIds.TENANT_ISOLATION));
		var proof = AuthorizationPreservationProof.create(source, rewritten);
		assertTrue(proof.isSatisfied());
		assertTrue(proof.violations().isEmpty());
	}

	@Test
	void droppedSecurityInvariantIsDetected() {
		var source = plan(List.of(SecurityInvariantIds.TENANT_ISOLATION, SecurityInvariantIds.AUTHORIZATION_REQUIRED));
		var rewritten = plan(List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED));
		var proof = AuthorizationPreservationProof.create(source, rewritten);
		assertFalse(proof.isSatisfied());
		assertEquals(1, proof.violations().size());
		assertTrue(proof.violations().get(0).contains(SecurityInvariantIds.TENANT_ISOLATION));
	}
}
