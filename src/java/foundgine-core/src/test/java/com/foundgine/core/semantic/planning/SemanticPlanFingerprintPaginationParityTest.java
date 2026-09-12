package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
import java.util.List;

class SemanticPlanFingerprintPaginationParityTest {
	private static SemanticPlan plan(Integer limit, Integer offset, String after) {
		var options = new SemanticQueryOptions(null, List.of(), limit, offset, after);
		var node = new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1), List.of(), null, null, List.of(),
				options, null, null, RelationshipTraversalMode.DEFAULT, -1, AggregateExecutionStrategy.DEFAULT);
		return new SemanticPlan(node);
	}

	@Test
	void exactFingerprintIncludesPaginationValues() {
		assertNotEquals(SemanticPlanFingerprint.create(plan(10, 0, null)),
				SemanticPlanFingerprint.create(plan(20, 0, null)));
	}

	@Test
	void shapeFingerprintParameterizesPaginationValues() {
		assertEquals(SemanticPlanFingerprint.createShapeKey(plan(10, 0, null)),
				SemanticPlanFingerprint.createShapeKey(plan(20, 100, "cursor-2")));
	}
}
