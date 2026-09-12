package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import org.junit.jupiter.api.Test;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of the model/graph hardening cases from
 * SemanticArchitectureHardeningTests.
 */
class SemanticArchitectureHardeningParityTest {
	@Test
	void relationshipIdentityIsStableWhenDerivedFromNames() {
		var customer = new EntityId(1);
		var order = new EntityId(2);
		var model = new SemanticModelBuilder()
				.entity(customer, "Customer",
						e -> e.identity(new FieldId(1), "Id").relationship("Orders", order,
								RelationshipCardinality.MANY))
				.entity(order, "Order", e -> e.identity(new FieldId(2), "Id")).build();
		assertEquals(RelationshipId.create("Customer", "Orders"), model.get(customer).relationships().get(0).id());
	}

	@Test
	void relationshipIdentityCollisionAcrossEntitiesFailsClosed() {
		var customer = new EntityId(1);
		var order = new EntityId(2);
		var shared = new RelationshipId(999);
		var builder = new SemanticModelBuilder()
				.entity(customer, "Customer",
						e -> e.identity(new FieldId(1), "Id").relationship(shared, "Orders", order,
								RelationshipCardinality.MANY))
				.entity(order, "Order", e -> e.identity(new FieldId(2), "Id").relationship(shared, "Customer", customer,
						RelationshipCardinality.ONE));
		var ex = assertThrows(IllegalStateException.class, builder::build);
		assertTrue(ex.getMessage().contains("Relationship identity"));
	}

	@Test
	void fieldConstraintsAreRetainedAndValidated() {
		var customer = new EntityId(1);
		var name = new FieldId(2);
		var credit = new FieldId(3);
		var model = new SemanticModelBuilder().entity(customer, "Customer", e -> e.identity(new FieldId(1), "Id")
				.field(name, "Name", String.class).field(credit, "CreditLimit", java.math.BigDecimal.class)
				.constraint(name, SemanticConstraint.pattern("^[A-Z]")).constraint(credit,
						SemanticConstraint.range(java.math.BigDecimal.ZERO, java.math.BigDecimal.valueOf(100000))))
				.build();
		assertTrue(model.get(customer).fields().stream().filter(f -> f.id().equals(name)).findFirst().orElseThrow()
				.effectiveConstraints().stream().anyMatch(c -> c.kind() == SemanticConstraintKind.PATTERN));
		assertTrue(model.get(customer).fields().stream().filter(f -> f.id().equals(credit)).findFirst().orElseThrow()
				.effectiveConstraints().stream().anyMatch(c -> c.kind() == SemanticConstraintKind.RANGE));
	}

	@Test
	void looseValidationAllowsMultipleRoots() {
		var model = new SemanticModelBuilder()
				.entity(new EntityId(1), "Customer", e -> e.identity(new FieldId(1), "Id"))
				.entity(new EntityId(2), "Order", e -> e.identity(new FieldId(2), "Id")).build();
		var graph = new SemanticGraph();
		graph.addRoot(new EntityId(1), List.of(new FieldId(1)));
		graph.addRoot(new EntityId(2), List.of(new FieldId(2)));
		SemanticGraphValidator.validate(graph, model, SemanticGraphValidationMode.LOOSE);
		assertThrows(IllegalStateException.class, () -> SemanticGraphValidator.validate(graph, model));
	}
}
