package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
import java.util.*;

class AggregateRewriteParityTest {
	private static SemanticPlan plan(SemanticFilterExpression filter) {
		var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1), List.of(), null, null, List.of(),
				new SemanticQueryOptions(filter, List.of(), null, null, null), null, null,
				RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT);
		return new SemanticPlan(node);
	}

	@Test
	void countExistenceRewritePreservesSemanticMeaning() {
		var before = plan(new SemanticAggregateFilter(new RelationshipId(10), SemanticFilterAggregate.COUNT, null,
				SemanticAggregateFilterOperator.GT, 0));
		var after = new AggregateCardinalityOptimizationRule().apply(before);
		assertTrue(SemanticEquivalenceProof.create(before, after).isSatisfied());
		assertEquals(AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT, after.root().aggregateExecutionStrategy());
	}

	@Test
	void countEqualityZeroUsesEmptyShortCircuit() {
		var before = plan(new SemanticAggregateFilter(new RelationshipId(10), SemanticFilterAggregate.COUNT, null,
				SemanticAggregateFilterOperator.EQ, 0));
		var after = new AggregateCardinalityOptimizationRule().apply(before);
		assertEquals(AggregateExecutionStrategy.COUNT_EMPTY_SHORT_CIRCUIT, after.root().aggregateExecutionStrategy());
	}

	@Test
	void unsupportedAggregateShapeIsNotRewritten() {
		var before = plan(new SemanticAggregateFilter(new RelationshipId(10), SemanticFilterAggregate.COUNT,
				new FieldId(4), SemanticAggregateFilterOperator.GT, 0));
		assertSame(before, new AggregateCardinalityOptimizationRule().apply(before));
	}
}
