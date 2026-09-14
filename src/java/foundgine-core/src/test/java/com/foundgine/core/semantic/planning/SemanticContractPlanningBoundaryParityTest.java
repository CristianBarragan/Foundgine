package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.ir.SemanticOperationCompiler;
import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class SemanticContractPlanningBoundaryParityTest {
	private static SemanticContractSnapshot contract() {
		var model = new SemanticModelBuilder()
				.entity(new EntityId(1), "Customer",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(10), "Name", String.class).relationship(
								new RelationshipId(1), "Orders", new EntityId(2), RelationshipCardinality.MANY))
				.entity(new EntityId(2), "Order",
						e -> e.identity(new FieldId(2), "Id").field(new FieldId(20), "Number", String.class))
				.build();
		return model.freeze().createSnapshot();
	}

	@Test
	void plannerAcceptsOperationBelongingToFrozenContract() {
		var graph = new SemanticGraph();
		var root = graph.addRoot(new EntityId(1), List.of(new FieldId(10)));
		graph.add(new EntityId(2), new RelationshipId(1), root, List.of(new FieldId(20)));
		var operation = SemanticOperationCompiler.compile(graph);
		var plan = new Planner().plan(contract(), operation);
		assertEquals(new EntityId(1), plan.root().entityId());
		assertEquals(new EntityId(2), plan.root().children().getFirst().entityId());
	}

	@Test
	void plannerRejectsUnknownEntityIdentity() {
		var graph = new SemanticGraph();
		graph.addRoot(new EntityId(999), List.of(new FieldId(1)));
		var operation = SemanticOperationCompiler.compile(graph);
		var ex = assertThrows(IllegalStateException.class, () -> new Planner().plan(contract(), operation));
		assertTrue(ex.getMessage().contains("999"));
	}

	@Test
	void plannerRejectsUnknownFieldIdentity() {
		var graph = new SemanticGraph();
		graph.addRoot(new EntityId(1), List.of(new FieldId(999)));
		var operation = SemanticOperationCompiler.compile(graph);
		var ex = assertThrows(IllegalStateException.class, () -> new Planner().plan(contract(), operation));
		assertTrue(ex.getMessage().toLowerCase(Locale.ROOT).contains("unknown"));
	}

	@Test
	void plannerRejectsRelationshipTargetThatDisagreesWithContract() {
		var graph = new SemanticGraph();
		var root = graph.addRoot(new EntityId(1));
		graph.add(new EntityId(3), new RelationshipId(1), root, List.of());
		var operation = SemanticOperationCompiler.compile(graph);
		var ex = assertThrows(IllegalStateException.class, () -> new Planner().plan(contract(), operation));
		assertTrue(ex.getMessage().toLowerCase(Locale.ROOT).contains("targets"));
	}
}
