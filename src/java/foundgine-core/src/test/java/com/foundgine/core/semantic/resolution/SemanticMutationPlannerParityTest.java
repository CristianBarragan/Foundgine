package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.mutation.*;
import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class SemanticMutationPlannerParityTest {
	@Test
	void dependencyCannotReferenceLaterOperation() {
		var customer = EntityId.create("Customer");
		var id = FieldId.create("Customer", "Id");
		var name = FieldId.create("Customer", "Name");
		var entity = new SemanticEntity(customer, "Customer", new SemanticFieldIdentity(id, "Id"),
				List.of(new SemanticField(id, "Id", Long.class), new SemanticField(name, "Name", String.class)),
				List.of());
		var model = new SemanticModel(Map.of(customer, entity), List.of()).freeze();
		var builder = new SemanticMutationIntentBuilder(model);
		builder.create("Customer", "first").set("Name", "Ada").returns("Id");
		builder.create("Customer", "second").setFrom("Name", "first", "Id");
		var graph = builder.build();
		var op = graph.operations().get(1);
		var bad = new SemanticMutationOperationGraph(List.of(op, graph.operations().get(0)));
		assertThrows(IllegalStateException.class, () -> new SemanticMutationPlanner().plan(bad));
	}

	@Test
	void dependencyMustReferenceReturnedField() {
		var customer = EntityId.create("Customer");
		var id = FieldId.create("Customer", "Id");
		var name = FieldId.create("Customer", "Name");
		var entity = new SemanticEntity(customer, "Customer", new SemanticFieldIdentity(id, "Id"),
				List.of(new SemanticField(id, "Id", Long.class), new SemanticField(name, "Name", String.class)),
				List.of());
		var model = new SemanticModel(Map.of(customer, entity), List.of()).freeze();
		var builder = new SemanticMutationIntentBuilder(model);
		builder.create("Customer", "first").set("Name", "Ada");
		builder.create("Customer", "second").setFrom("Name", "first", "Id");
		var graph = builder.build();
		assertThrows(IllegalStateException.class, () -> new SemanticMutationPlanner().plan(graph));
	}
}