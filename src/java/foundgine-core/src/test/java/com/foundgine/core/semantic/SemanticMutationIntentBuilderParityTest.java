package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.mutation.*;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

/** Mirrors Foundgine.Semantics.Tests/SemanticMutationIntentBuilderTests.cs. */
class SemanticMutationIntentBuilderParityTest {
	private static final EntityId CUSTOMER = new EntityId(1);
	private static final EntityId ACCOUNT = new EntityId(2);
	private static final FieldId ID = new FieldId(1);
	private static final FieldId NAME = new FieldId(2);
	private static final FieldId CUSTOMER_ID = new FieldId(3);
	private static final FieldId STATUS = new FieldId(4);

	@Test
	void openMutationBuilderSupportsGeneratedValueDependenciesWithoutProviderConcepts() {
		var graph = new SemanticMutationIntentBuilder(buildModel()).create("Customer", "customer").set("Name", "Alice")
				.returns("Id").create("Account", "account").setFrom("CustomerId", "customer", "Id")
				.set("Status", "Open").returns("Id", "CustomerId").build();

		var plan = new SemanticMutationPlanner().plan(graph);
		assertEquals(2, plan.operations().size());
		assertEquals(1, plan.dependencies().size());
		assertEquals(ID, plan.dependencies().get(0).sourceField());
		assertEquals(CUSTOMER_ID, plan.dependencies().get(0).targetField());
	}

	@Test
	void openMutationBuilderPreservesUpdateFiltersAndConflictSemantics() {
		var graph = new SemanticMutationIntentBuilder(buildModel()).upsert("Account").set("CustomerId", 42)
				.set("Status", "Open").conflict("CustomerId").returns("Id", "Status").update("Customer")
				.set("Name", "Verified").where("Id", SemanticFilterOperator.EQ, 42).returns("Id").build();

		var plan = new SemanticMutationPlanner().plan(graph);
		assertEquals(java.util.List.of(CUSTOMER_ID), plan.operations().get(0).conflictFields());
		assertInstanceOf(com.foundgine.core.semantic.query.SemanticFieldFilter.class,
				plan.operations().get(1).filter());
		assertEquals(java.util.List.of(ID), plan.operations().get(1).returnFields());
	}

	private static SemanticModel buildModel() {
		return new SemanticModelBuilder()
				.entity(CUSTOMER, "Customer", e -> e.identity(ID, "Id").field(NAME, "Name", String.class))
				.entity(ACCOUNT, "Account", e -> e.identity(new FieldId(5), "Id")
						.field(CUSTOMER_ID, "CustomerId", Long.class).field(STATUS, "Status", String.class))
				.build();
	}
}
