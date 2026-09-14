package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/** Port of {@code SemanticEquivalenceProofTests} (Foundgine.Planning.Tests). */
class SemanticEquivalenceProofParityTest {

	@Test
	void optimizerProducesSatisfiedSemanticEquivalenceProof() {
		var predicate = AuthorizationPredicate.and(
				AuthorizationPredicate.equal(AuthorizationPredicate.resourceParameter("tenant"),
						AuthorizationPredicate.constant("nz")),
				AuthorizationPredicate.equal(AuthorizationPredicate.resourceParameter("region"),
						AuthorizationPredicate.constant("north")));

		var plan = new SemanticPlan(nodeWithAuthorization(predicate),
				List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED), null);

		var result = new SemanticPlanOptimizer().optimize(plan);

		assertTrue(result.semanticProof().isSatisfied());
		assertEquals(SemanticEquivalenceFingerprint.create(plan), result.semanticProof().beforeFingerprint());
		assertEquals(SemanticEquivalenceFingerprint.create(result.plan()), result.semanticProof().afterFingerprint());
	}

	@Test
	void authorizationOperandReorderingIsSemanticallyEquivalent() {
		var a = AuthorizationPredicate.equal(AuthorizationPredicate.resourceParameter("a"),
				AuthorizationPredicate.constant("1"));
		var b = AuthorizationPredicate.equal(AuthorizationPredicate.resourceParameter("b"),
				AuthorizationPredicate.constant("2"));

		var first = new SemanticPlan(nodeWithAuthorization(AuthorizationPredicate.and(a, b)));
		var second = new SemanticPlan(nodeWithAuthorization(AuthorizationPredicate.and(b, a)));

		assertTrue(SemanticEquivalenceProof.create(first, second).isSatisfied());
	}

	@Test
	void meaningfulFieldChangeIsNotSemanticallyEquivalent() {
		var first = new SemanticPlan(node(List.of(new FieldId(1)), null));
		var second = new SemanticPlan(node(List.of(new FieldId(2)), null));

		assertThrows(IllegalStateException.class, () -> SemanticEquivalenceProof.create(first, second));
	}

	@Test
	void meaningfulPaginationChangeIsNotSemanticallyEquivalent() {
		var first = new SemanticPlan(
				node(List.of(new FieldId(1)), null, new SemanticQueryOptions(null, List.of(), 10, null, null)));
		var second = new SemanticPlan(
				node(List.of(new FieldId(1)), null, new SemanticQueryOptions(null, List.of(), 20, null, null)));

		assertThrows(IllegalStateException.class, () -> SemanticEquivalenceProof.create(first, second));
	}

	@Test
	void securityContractChangeIsNotSemanticallyEquivalent() {
		var first = new SemanticPlan(node(List.of(new FieldId(1)), null),
				List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED), null);
		var second = new SemanticPlan(node(List.of(new FieldId(1)), null),
				List.of(SecurityInvariantIds.TENANT_ISOLATION), null);

		assertThrows(IllegalStateException.class, () -> SemanticEquivalenceProof.create(first, second));
	}

	private static SemanticPlanNode nodeWithAuthorization(AuthorizationPredicate authorization) {
		return node(List.of(new FieldId(1)), authorization);
	}

	private static SemanticPlanNode node(List<FieldId> fields, AuthorizationPredicate authorization) {
		return node(fields, authorization, null);
	}

	private static SemanticPlanNode node(List<FieldId> fields, AuthorizationPredicate authorization,
			SemanticQueryOptions options) {
		return new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1), fields, null, null, List.of(), options,
				authorization, null, RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT);
	}
}
