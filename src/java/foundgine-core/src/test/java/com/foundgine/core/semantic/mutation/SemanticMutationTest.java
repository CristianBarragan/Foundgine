package com.foundgine.core.semantic.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
import java.util.*;

class SemanticMutationTest {
	private static SemanticModel model() {
		var id = new FieldId(1);
		var name = new FieldId(2);
		var customer = new EntityId(10);
		var entity = new SemanticEntity(customer, "Customer", new SemanticFieldIdentity(id, "Id"),
				List.of(new SemanticField(id, "Id", Long.class), new SemanticField(name, "Name", String.class)),
				List.of());
		return new SemanticModel(Map.of(customer, entity), List.of()).freeze();
	}

	@Test
	void fluentBuilderResolvesNamesAndBuildsCanonicalGraph() {
		var graph = new SemanticMutationIntentBuilder(model()).create("Customer", "customer").set("Name", "Ada")
				.returns("Id").build();
		assertEquals(1, graph.operations().size());
		assertEquals(SemanticMutationKind.CREATE, graph.operations().get(0).kind());
		assertEquals(new FieldId(2), graph.operations().get(0).fields().get(0).field());
		assertEquals(List.of(new FieldId(1)), graph.operations().get(0).returnFields());
	}

	@Test
	void dependencyRequiresReturnedSourceField() {
		var b = new SemanticMutationIntentBuilder(model());
		b.create("Customer", "source").set("Name", "Ada").returns("Id");
		b.create("Customer", "target").setFrom("Name", "source", "Id").returns("Id");
		var plan = new SemanticMutationPlanner().plan(b.build());
		assertEquals(1, plan.dependencies().size());
		assertEquals("0", plan.dependencies().get(0).fromOperationId());
		assertEquals("1", plan.dependencies().get(0).toOperationId());
	}

	@Test
	void updateRequiresFilter() {
		var b = new SemanticMutationIntentBuilder(model());
		b.update("Customer").set("Name", "Ada");
		assertThrows(IllegalStateException.class, b::build);
	}

	@Test
	void aggregateLegalityRejectsCountToMin() {
		var r = com.foundgine.core.semantic.aggregates.AggregateRewriteLegality
				.checkSubstitution(SemanticFilterAggregate.COUNT, SemanticFilterAggregate.MIN);
		assertFalse(r.isLegal());
		assertTrue(r.violations().size() >= 2);
	}
}
