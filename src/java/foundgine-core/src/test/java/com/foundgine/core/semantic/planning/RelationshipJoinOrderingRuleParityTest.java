package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of {@code RelationshipJoinOrderingRuleTests} (Foundgine.Planning.Tests).
 */
class RelationshipJoinOrderingRuleParityTest {

	@Test
	void ordersRelationshipTraversalsWithoutReorderingLogicalChildren() {
		var first = node(2, 20, RelationshipCardinality.MANY, null);
		var second = node(3, 10, RelationshipCardinality.ONE,
				new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.EQ, 1));
		var root = new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1), List.of(new FieldId(1)), null,
				null, List.of(first, second));
		var before = new SemanticPlan(root);

		var after = new RelationshipJoinOrderingRule().apply(before);

		assertEquals(before.root().children().get(0).viaRelationship(),
				after.root().children().get(0).viaRelationship());
		assertEquals(0, after.root().children().get(1).traversalOrder());
		assertEquals(1, after.root().children().get(0).traversalOrder());
		assertNotEquals(after.root().children().get(0).viaRelationship(),
				after.root().children().get(1).viaRelationship());
	}

	@Test
	void doesNotApplyWhenFewerThanTwoEligibleRelationshipsExist() {
		var child = node(2, 20, RelationshipCardinality.MANY, null);
		var plan = new SemanticPlan(new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
				List.of(new FieldId(1)), null, null, List.of(child)));

		var after = new RelationshipJoinOrderingRule().apply(plan);

		assertSame(plan, after);
	}

	@Test
	void preservesSemanticEquivalence() {
		var first = node(2, 20, RelationshipCardinality.MANY, null);
		var second = node(3, 10, RelationshipCardinality.ONE, null);
		var before = new SemanticPlan(new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
				List.of(new FieldId(1)), null, null, List.of(first, second)));
		var after = new RelationshipJoinOrderingRule().apply(before);

		var proof = SemanticEquivalenceProof.create(before, after);

		assertTrue(proof.isSatisfied());
	}

	@Test
	void isIdempotent() {
		var first = node(2, 20, RelationshipCardinality.MANY, null);
		var second = node(3, 10, RelationshipCardinality.ONE, null);
		var before = new SemanticPlan(new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
				List.of(new FieldId(1)), null, null, List.of(first, second)));
		var rule = new RelationshipJoinOrderingRule();

		var once = rule.apply(before);
		var twice = rule.apply(once);

		assertSame(once, twice);
	}

	private static SemanticPlanNode node(int id, long relationshipId, RelationshipCardinality cardinality,
			SemanticFilterExpression filter) {
		var options = filter == null ? null : new SemanticQueryOptions(filter, null, null, null, null);
		return new SemanticPlanNode(id, ExecutionOperation.TRAVERSE, new EntityId(id), List.of(new FieldId(id)),
				new RelationshipId(relationshipId), null, List.of(), options, null, cardinality,
				RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT);
	}
}
