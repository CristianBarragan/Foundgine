package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.metadata.ColumnMetadata;
import com.foundgine.core.semantic.metadata.ColumnReference;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import com.foundgine.core.semantic.metadata.SemanticModelDiscovery;
import org.junit.jupiter.api.Test;

import java.math.BigDecimal;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of {@code Foundgine.Core.Semantic.Tests.SemanticModelTests}.
 *
 * <p>
 * <b>Porting decision:</b> most of the C# fixture exercises typed builder
 * overloads ({@code Entity<T>(...)}, {@code Field(x => x.Name)},
 * {@code Relationship<T1, T2>(...)}) that rely on C# expression trees to
 * derive field/relationship identity from domain model properties. Java has
 * no equivalent typed/expression-tree builder — only the untyped
 * {@code SemanticModelBuilder} exists — so this port covers every scenario
 * that is expressible through the untyped builder and the metadata-discovery
 * path (which was never typed to begin with), and omits the typed-selector
 * scenarios (field/relationship derivation from POCO properties, mismatched
 * property-type rejection) that have no Java counterpart.
 */
class SemanticModelParityTest {

	@Test
	void bankingDomainCanBeDescribedAsSemanticModel() {
		var customer = new EntityId(1);
		var account = new EntityId(2);
		var transaction = new EntityId(3);

		var model = new SemanticModelBuilder()
				.entity(customer, "Customer",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Name", String.class)
								.relationship(new RelationshipId(1), "Accounts", account, RelationshipCardinality.MANY))
				.entity(account, "Account",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(3), "Balance", BigDecimal.class)
								.relationship(new RelationshipId(2), "Transactions", transaction,
										RelationshipCardinality.MANY))
				.entity(transaction, "Transaction",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(3), "Amount", BigDecimal.class))
				.build();

		assertEquals(3, model.entities().size());
		assertEquals("Customer", model.get(customer).name());
		assertEquals(account, model.get(customer).relationships().get(0).target());
		assertEquals(transaction, model.get(account).relationships().get(0).target());
	}

	@Test
	void metadataDiscoveryBuildsStructuralSemanticsWithoutManualEntityRegistration() {
		var registry = new MetadataRegistry();
		var customer = new EntityId(200);
		var order = new EntityId(201);
		var customerId = new FieldId(1);
		var customerPk = new ColumnId(1);
		var orderPk = new ColumnId(2);
		var relationship = new RelationshipId(200);

		registry.register(new EntityMetadata(customer, "Customer", List.of(new ColumnMetadata(customerPk, "Id")),
				null,
				List.of(new FieldMetadata(customerId, "Id", int.class, new ColumnReference(customer, customerPk)),
						new FieldMetadata(new FieldId(3), "Name", String.class)),
				new ColumnReference(customer, customerPk), null, false, null, null));

		registry.register(new EntityMetadata(order, "Order", List.of(new ColumnMetadata(orderPk, "Id")), null,
				List.of(new FieldMetadata(new FieldId(2), "Id", int.class, new ColumnReference(order, orderPk))),
				new ColumnReference(order, orderPk), null, false, null, null));

		registry.register(new RelationshipMetadata(relationship, customer, order, "Orders",
				new ColumnReference(customer, customerPk), new ColumnReference(order, orderPk), true, null));

		var model = SemanticModelDiscovery.discover(registry);

		assertEquals(customerId, model.get(customer).identity().fieldId());
		assertTrue(model.get(customer).fields().stream().anyMatch(field -> field.name().equals("Name")));
		assertEquals(1, model.get(customer).relationships().size());
		var discoveredRelationship = model.get(customer).relationships().get(0);
		assertEquals("Orders", discoveredRelationship.name());
		assertEquals(RelationshipCardinality.MANY, discoveredRelationship.cardinality());
	}

	@Test
	void metadataDiscoveryCanBeEnrichedWithLogicalTraversals() {
		var registry = new MetadataRegistry();
		var customer = new EntityId(210);
		var relationshipEntity = new EntityId(211);
		var contract = new EntityId(212);
		var transaction = new EntityId(213);

		registerEntity(registry, customer, "Customer", 210);
		registerEntity(registry, relationshipEntity, "CustomerRelationship", 211);
		registerEntity(registry, contract, "Contract", 212);
		registerEntity(registry, transaction, "Transaction", 213);

		registry.register(new RelationshipMetadata(new RelationshipId(210), customer, relationshipEntity,
				"Relationships", new ColumnReference(customer, new ColumnId(210)),
				new ColumnReference(relationshipEntity, new ColumnId(211))));
		registry.register(new RelationshipMetadata(new RelationshipId(211), relationshipEntity, contract, "Contract",
				new ColumnReference(relationshipEntity, new ColumnId(211)),
				new ColumnReference(contract, new ColumnId(212))));
		registry.register(new RelationshipMetadata(new RelationshipId(212), contract, transaction, "Transactions",
				new ColumnReference(contract, new ColumnId(212)), new ColumnReference(transaction, new ColumnId(213))));

		var model = SemanticModelDiscovery.fromMetadata(registry)
				.traversal(customer, "transactions", new RelationshipId(210), new RelationshipId(211),
						new RelationshipId(212))
				.build();

		var traversal = model.getTraversal(customer, "transactions");
		assertEquals(transaction, traversal.target());
		assertEquals(3, traversal.path().size());
	}

	private static void registerEntity(MetadataRegistry registry, EntityId id, String name, int columnId) {
		var column = new ColumnId(columnId);
		registry.register(new EntityMetadata(id, name, List.of(new ColumnMetadata(column, "Id")), null,
				List.of(new FieldMetadata(new FieldId(columnId), "Id", int.class, new ColumnReference(id, column))),
				new ColumnReference(id, column), null, false, null, null));
	}

	@Test
	void requestGraphIsProviderIndependent() {
		var graph = new SemanticGraph();
		var customer = graph.addRoot(new EntityId(1));
		var account = graph.add(new EntityId(2), new RelationshipId(1), customer);
		var transaction = graph.add(new EntityId(3), new RelationshipId(2), account);

		assertEquals(3, graph.nodes().size());
		assertNull(customer.parentId());
		assertEquals(customer.id(), account.parentId());
		assertEquals(account.id(), transaction.parentId());
	}

	@Test
	void entityIdsAreDeterministicAndOrderIndependent() {
		var customer = EntityId.create("Customer");
		var account = EntityId.create("Account");

		assertEquals(customer, EntityId.create("Customer"));
		assertEquals(account, EntityId.create("Account"));
		assertNotEquals(customer, account);
	}

	@Test
	void snapshotRequiresAnExplicitlyFrozenModel() {
		var model = new SemanticModelBuilder()
				.entity(new EntityId(301), "Customer", e -> e.identity(new FieldId(1), "Id")).build();

		assertThrows(IllegalStateException.class, model::createSnapshot);
	}

	@Test
	void snapshotPreservesFingerprintAndIsIndependentOfModelState() {
		var customer = new EntityId(302);
		var order = new EntityId(303);
		var model = new SemanticModelBuilder()
				.entity(customer, "Customer",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Name", String.class)
								.relationship(new RelationshipId(302), "ordersRelationship", order,
										RelationshipCardinality.MANY))
				.entity(order, "Order", e -> e.identity(new FieldId(1), "Id"))
				.traversal(customer, "orders", new RelationshipId(302)).build().freeze();

		var snapshot = model.createSnapshot();

		assertEquals(model.contractFingerprint(), snapshot.contractFingerprint());
		assertEquals("Customer", snapshot.get(customer).name());
		assertEquals("Name", snapshot.get(customer).fields().stream().filter(f -> f.name().equals("Name")).findFirst()
				.orElseThrow().name());
		assertEquals(order, snapshot.getTraversal(customer, "orders").target());
		assertThrows(UnsupportedOperationException.class, () -> snapshot.traversals().set(0, null));
	}

	@Test
	void snapshotDefensivelyCopiesNestedCollections() {
		var customer = new EntityId(304);
		var model = new SemanticModelBuilder().entity(customer, "Customer",
				e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Name", String.class)
						.fieldAlias(new FieldId(2), "display").constraint(new FieldId(2), SemanticConstraint.pattern(".+")))
				.build().freeze();

		var snapshot = model.createSnapshot();
		var entity = snapshot.get(customer);
		var field = entity.fields().get(0);

		assertThrows(UnsupportedOperationException.class, () -> entity.fields().clear());
		assertThrows(UnsupportedOperationException.class,
				() -> field.effectiveAliases().set(0, new SemanticAlias("changed")));
		assertThrows(UnsupportedOperationException.class,
				() -> field.effectiveConstraints().set(0, SemanticConstraint.pattern("changed")));
	}
}