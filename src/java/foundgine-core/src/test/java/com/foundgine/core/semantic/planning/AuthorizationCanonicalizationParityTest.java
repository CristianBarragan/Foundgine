package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
import java.util.*;

class AuthorizationCanonicalizationParityTest {
	private static SemanticPlan plan(AuthorizationPredicate predicate) {
		var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1), List.of(), null, null, List.of(),
				null, predicate, null, RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT);
		return new SemanticPlan(node);
	}

	@Test
	void duplicateAndOperandsAreCanonicalized() {
		var a = AuthorizationPredicate.parameter("tenant");
		var b = AuthorizationPredicate.parameter("role");
		var before = plan(AuthorizationPredicate.and(AuthorizationPredicate.and(a, b), a));
		var after = new AuthorizationCanonicalizationRule().apply(before);
		assertNotSame(before.root().authorization(), after.root().authorization());
		assertTrue(SemanticEquivalenceProof.create(before, after).isSatisfied());
	}

	@Test
	void doubleNegationIsRemoved() {
		var a = AuthorizationPredicate.parameter("tenant");
		var before = plan(AuthorizationPredicate.not(AuthorizationPredicate.not(a)));
		var after = new AuthorizationCanonicalizationRule().apply(before);
		assertEquals(a, after.root().authorization());
	}
}
