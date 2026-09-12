package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.query.*;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import org.junit.jupiter.api.Test;

import java.util.*;

import static org.junit.jupiter.api.Assertions.*;

/** Port of {@code PredicatePushdownRuleTests} (Foundgine.Planning.Tests). */
class PredicatePushdownRuleParityTest {

	@Test
	void distributesConjunctIntoOrBranches() {
		var a = new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.EQ, "A");
		var b = new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.EQ, "B");
		var c = new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.EQ, "C");

		var filter = new SemanticAndFilter(List.of(new SemanticOrFilter(List.of(a, b)), c));
		var result = new PredicatePushdownRule().apply(plan(filter));

		var rewritten = assertInstanceOf(SemanticOrFilter.class, result.root().queryOptions().filter());
		assertEquals(2, rewritten.expressions().size());
		assertTrue(rewritten.expressions().stream()
				.allMatch(expression -> expression instanceof SemanticAndFilter and && and.expressions().contains(c)));
	}

	@Test
	void rewriteIsSemanticallyEquivalent() {
		var a = new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.EQ, "A");
		var b = new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.EQ, "B");
		var c = new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.EQ, "C");
		var filter = new SemanticAndFilter(List.of(new SemanticOrFilter(List.of(a, b)), c));

		var before = plan(filter);
		var after = new PredicatePushdownRule().apply(before);

		assertTrue(SemanticEquivalenceProof.create(before, after).isSatisfied());
	}

	@Test
	void rewritePreservesSecurityInvariants() {
		var filter = new SemanticAndFilter(List.of(
				new SemanticOrFilter(List.of(new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.EQ, "A"),
						new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.EQ, "B"))),
				new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.EQ, "C")));

		var before = new SemanticPlan(planNode(filter),
				List.of(SecurityInvariantIds.AUTHORIZATION_REQUIRED, SecurityInvariantIds.RUNTIME_AUTHORIZATION), null);
		var after = new PredicatePushdownRule().apply(before);

		assertTrue(SecurityPreservationProof.create(before, after).isSatisfied());
	}

	@Test
	void doesNotExpandBeyondRuleBudget() {
		var branches = new ArrayList<SemanticFilterExpression>();
		for (int i = 1; i <= 17; i++) {
			branches.add(new SemanticFieldFilter(new FieldId(i), SemanticFilterOperator.EQ, i));
		}
		var filter = new SemanticAndFilter(List.of(new SemanticOrFilter(branches),
				new SemanticFieldFilter(new FieldId(100), SemanticFilterOperator.EQ, 100)));

		var before = plan(filter);
		assertSame(before, new PredicatePushdownRule().apply(before));
	}

	private static SemanticPlan plan(SemanticFilterExpression filter) {
		return new SemanticPlan(planNode(filter));
	}

	private static SemanticPlanNode planNode(SemanticFilterExpression filter) {
		return new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
				List.of(new FieldId(1), new FieldId(2), new FieldId(3)), null, null, List.of(),
				new SemanticQueryOptions(filter, List.of(), null, null, null), null, null,
				RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT);
	}
}
