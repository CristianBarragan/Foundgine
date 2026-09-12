package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.ir.*;
import java.util.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class PlannerTest {
	@Test
	void rootBecomesScanAndChildrenBecomeTraversal() {
		var customer = new EntityId(1);
		var order = new EntityId(2);
		var f = new FieldId(11);
		var r = new RelationshipId(21);
		var child = new SemanticReadNode(2, order, List.of(f), r, null, List.of(), null, null);
		var root = new SemanticReadNode(1, customer, List.of(f), null, null, List.of(child), null, null);
		var plan = new Planner().plan(new SemanticOperation(root));
		assertEquals(ExecutionOperation.SCAN, plan.root().operation());
		assertEquals(ExecutionOperation.TRAVERSE, plan.root().children().get(0).operation());
		assertEquals(r, plan.root().children().get(0).viaRelationship());
	}

	@Test
	void rejectsNonRootWithoutParentEdge() {
		var n = new SemanticReadNode(1, new EntityId(1), List.of(), null, null, List.of(), null, null);
		assertThrows(IllegalArgumentException.class, () -> new Planner().plan(new SemanticOperation(
				new SemanticReadNode(0, new EntityId(2), List.of(), null, null, List.of(n), null, null))));
	}

	@Test
	void rejectsNodeWithBothRelationshipAndConnection() {
		var n = new SemanticReadNode(1, new EntityId(1), List.of(), new RelationshipId(1), new ConnectionId(2),
				List.of(), null, null);
		assertThrows(IllegalArgumentException.class, () -> new Planner().plan(new SemanticOperation(n)));
	}

	@Test
	void detectsDuplicateNodeIds() {
		var a = new SemanticReadNode(2, new EntityId(2), List.of(), new RelationshipId(1), null, List.of(), null, null);
		var b = new SemanticReadNode(2, new EntityId(3), List.of(), new RelationshipId(2), null, List.of(), null, null);
		var root = new SemanticReadNode(1, new EntityId(1), List.of(), null, null, List.of(a, b), null, null);
		assertThrows(IllegalArgumentException.class, () -> new Planner().plan(new SemanticOperation(root)));
	}
}
