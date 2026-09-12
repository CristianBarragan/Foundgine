package com.foundgine.core.execution.e2e;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.planning.*;
import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class ResultMaterializationParityE2ETest {
	private static final EntityId CUSTOMER = EntityId.create("Customer");
	private static final EntityId ORDER = EntityId.create("Order");
	private static final FieldId CUSTOMER_ID = FieldId.create("Customer", "Id");
	private static final FieldId CUSTOMER_NAME = FieldId.create("Customer", "Name");
	private static final FieldId ORDER_ID = FieldId.create("Order", "Id");
	private static final FieldId ORDER_NUMBER = FieldId.create("Order", "Number");
	private static final RelationshipId ORDERS = RelationshipId.create("Customer", "orders");

	private SemanticModel model() {
		return new SemanticModelBuilder()
				.entity(CUSTOMER, "Customer",
						e -> e.identity(CUSTOMER_ID, "Id").field(CUSTOMER_ID, "Id", Integer.class)
								.field(CUSTOMER_NAME, "Name", String.class)
								.relationship(ORDERS, "orders", ORDER, RelationshipCardinality.MANY))
				.entity(ORDER, "Order", e -> e.identity(ORDER_ID, "Id").field(ORDER_ID, "Id", Integer.class)
						.field(ORDER_NUMBER, "Number", String.class))
				.build();
	}

	@Test
	void materializerCollapsesRepeatedProviderRowsIntoOneRoot() {
		var child = new SemanticPlanNode(2, ExecutionOperation.TRAVERSE, ORDER, List.of(ORDER_ID, ORDER_NUMBER), ORDERS,
				null, List.of());
		var root = new SemanticPlanNode(1, ExecutionOperation.SCAN, CUSTOMER, List.of(CUSTOMER_ID, CUSTOMER_NAME), null,
				null, List.of(child));
		var plan = new SemanticPlan(root);
		var cells = new LinkedHashMap<ExecutionCellKey, Object>();
		cells.put(new ExecutionCellKey(1, CUSTOMER, CUSTOMER_ID), 7);
		cells.put(new ExecutionCellKey(1, CUSTOMER, CUSTOMER_NAME), "Alice");
		cells.put(new ExecutionCellKey(2, ORDER, ORDER_ID), 101);
		cells.put(new ExecutionCellKey(2, ORDER, ORDER_NUMBER), "PO-101");
		var row1 = new ExecutionRow(Map.of(), cells);
		var row2 = new ExecutionRow(Map.of(), cells);

		var result = new ResultMaterializer(model()).materialize(plan, new ExecutionResult(List.of(row1, row2)));
		assertEquals(1, result.roots().size());
		assertEquals(7, result.roots().get(0).identityValue());
		assertEquals(1, result.roots().get(0).children().get(ORDERS).size());
		assertEquals("PO-101", result.roots().get(0).children().get(ORDERS).get(0).values().get(ORDER_NUMBER));
	}

	@Test
	void materializerRejectsNullRootIdentityButIgnoresNullChildIdentity() {
		var child = new SemanticPlanNode(2, ExecutionOperation.TRAVERSE, ORDER, List.of(ORDER_ID), ORDERS, null,
				List.of());
		var root = new SemanticPlanNode(1, ExecutionOperation.SCAN, CUSTOMER, List.of(CUSTOMER_ID), null, null,
				List.of(child));
		var plan = new SemanticPlan(root);

		var rootCells = new LinkedHashMap<ExecutionCellKey, Object>();
		rootCells.put(new ExecutionCellKey(1, CUSTOMER, CUSTOMER_ID), 7);
		rootCells.put(new ExecutionCellKey(2, ORDER, ORDER_ID), null);
		var materialized = new ResultMaterializer(model()).materialize(plan,
				new ExecutionResult(List.of(new ExecutionRow(Map.of(), rootCells))));
		assertEquals(1, materialized.roots().size());
		assertTrue(materialized.roots().get(0).children().getOrDefault(ORDERS, List.of()).isEmpty());

		var missingRoot = Map.<ExecutionCellKey, Object>of();
		assertThrows(IllegalStateException.class, () -> new ResultMaterializer(model()).materialize(plan,
				new ExecutionResult(List.of(new ExecutionRow(Map.of(), missingRoot)))));
	}
}
