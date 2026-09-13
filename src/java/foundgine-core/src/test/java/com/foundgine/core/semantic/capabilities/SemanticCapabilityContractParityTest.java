package com.foundgine.core.semantic.capabilities;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.abstractions.AuthorizationAccess;
import com.foundgine.core.abstractions.AuthorizationOperation;
import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationCapabilityDiscovery;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of
 * {@code Foundgine.Core.Semantic.Tests.SemanticCapabilityContractTests}.
 *
 * <p>
 * Locks the semantic capability contract used by application and AI adapters.
 * These tests deliberately stay provider- and transport-independent.
 */
class SemanticCapabilityContractParityTest {

	private static final class ReadOnlyPolicy extends AllowAllSemanticAuthorizationPolicy {
		@Override
		public boolean canWriteEntity(EntityId entityId) {
			return false;
		}

		@Override
		public boolean canWriteField(EntityId entityId, FieldId fieldId) {
			return false;
		}
	}

	private static final class ConditionalCustomerPolicy extends AllowAllSemanticAuthorizationPolicy {
		@Override
		public AuthorizationPredicate getPredicate(EntityId entityId, AuthorizationOperation operation) {
			return entityId.equals(new EntityId(1)) && operation == AuthorizationOperation.READ
					? AuthorizationPredicate.equal(
							AuthorizationPredicate.member(AuthorizationPredicate.parameter("customer"), "TenantId"),
							AuthorizationPredicate.contextParameter("tenantId"))
					: null;
		}
	}

	@Test
	void capabilityDiscoveryIsDeterministicallyOrdered() {
		var model = new SemanticModelBuilder()
				.entity(new EntityId(2), "Zebra",
						e -> e.identity(new FieldId(20), "Id").field(new FieldId(22), "Name", String.class))
				.entity(new EntityId(1), "Account",
						e -> e.identity(new FieldId(10), "Id").field(new FieldId(12), "Name", String.class))
				.build();

		var capabilities = SemanticAuthorizationCapabilityDiscovery.describe(model,
				new AllowAllSemanticAuthorizationPolicy());

		assertEquals(java.util.List.of("Account", "Zebra"),
				capabilities.entities().stream().map(x -> x.name()).toList());
		assertEquals(java.util.List.of("Name"), capabilities.entities().get(0).fields().stream()
				.map(x -> x.name()).toList());
	}

	@Test
	void conditionalAuthorizationIsDescribedWithoutExposingThePredicate() {
		var model = new SemanticModelBuilder()
				.entity(new EntityId(1), "Customer",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "TenantId", Integer.class))
				.build();

		var capabilities = SemanticAuthorizationCapabilityDiscovery.describe(model, new ConditionalCustomerPolicy());

		assertEquals(1, capabilities.entities().size());
		var customer = capabilities.entities().get(0);
		assertEquals(AuthorizationAccess.CONDITIONAL, customer.read().access());
		assertNull(customer.read().predicate());
	}

	@Test
	void canonicalContractContainsReadWriteAndTraversalCapabilities() {
		var model = new SemanticModelBuilder()
				.entity(new EntityId(1), "Customer",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Name", String.class)
								.relationship(new RelationshipId(1), "Accounts", new EntityId(2),
										RelationshipCardinality.MANY))
				.entity(new EntityId(2), "Account",
						e -> e.identity(new FieldId(3), "Id").field(new FieldId(4), "Balance", java.math.BigDecimal.class))
				.build();

		var contract = SemanticCapabilityContractDiscovery.describe(model, new AllowAllSemanticAuthorizationPolicy());

		assertEquals(SemanticCapabilityContractDiscovery.CURRENT_VERSION, contract.version());
		assertTrue(contract.capabilities().stream()
				.anyMatch(x -> x.id().equals("Customer.read") && x.fields().contains("Name")));
		assertTrue(contract.capabilities().stream()
				.anyMatch(x -> x.id().equals("Customer.write") && x.fields().contains("Name")));

		var traversal = contract.capabilities().stream().filter(x -> x.id().equals("Customer.Accounts.traverse"))
				.toList();
		assertEquals(1, traversal.size());
		assertEquals(new EntityId(2), traversal.get(0).targetEntityId());
		assertEquals(AuthorizationAccess.ALLOWED, traversal.get(0).access().access());
	}

	@Test
	void canonicalContractExposesExplicitMutationActionsAndSemanticConstraints() {
		var model = new SemanticModelBuilder()
				.entity(new EntityId(1), "Order",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Total", java.math.BigDecimal.class))
				.build();

		var contract = SemanticCapabilityContractDiscovery.describe(model, new AllowAllSemanticAuthorizationPolicy());

		var update = single(contract, "Order.update");
		assertEquals("update", update.operation());
		assertTrue(update.hasSideEffects());
		assertTrue(update.isIdempotent());
		assertTrue(update.constraints().stream().anyMatch(x -> x.name().equals("target-selection")));
		assertTrue(update.constraints().stream().anyMatch(x -> x.name().equals("writable-fields")));
		assertTrue(update.effects().stream().anyMatch(x -> x.name().equals("data.update")));

		var create = single(contract, "Order.create");
		assertEquals("create", create.operation());
		assertFalse(create.isIdempotent());

		var delete = single(contract, "Order.delete");
		assertEquals("delete", delete.operation());
		assertTrue(delete.constraints().stream().anyMatch(x -> x.name().equals("target-selection")));

		var upsert = single(contract, "Order.upsert");
		assertEquals("upsert", upsert.operation());
		assertTrue(upsert.constraints().stream().anyMatch(x -> x.name().equals("conflict-key")));
	}

	@Test
	void canonicalContractPreservesPolicyScopedAccess() {
		var model = new SemanticModelBuilder()
				.entity(new EntityId(1), "Customer",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Name", String.class))
				.build();

		var contract = SemanticCapabilityContractDiscovery.describe(model, new ReadOnlyPolicy());

		var read = single(contract, "Customer.read");
		var write = single(contract, "Customer.write");

		assertEquals(AuthorizationAccess.ALLOWED, read.access().access());
		assertEquals(AuthorizationAccess.DENIED, write.access().access());
		assertTrue(write.effects().isEmpty());
	}

	@Test
	void capabilityDocumentIsMachineSerializableWithoutProviderTypes() throws Exception {
		var model = new SemanticModelBuilder()
				.entity(new EntityId(1), "Customer",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Name", String.class))
				.build();

		var capabilities = SemanticAuthorizationCapabilityDiscovery.describe(model,
				new AllowAllSemanticAuthorizationPolicy());

		var json = new ObjectMapper().writeValueAsString(capabilities);

		assertTrue(json.contains("Customer"));
		assertTrue(json.contains("Name"));
		assertFalse(json.toUpperCase(java.util.Locale.ROOT).contains("SELECT "));
		assertFalse(json.toUpperCase(java.util.Locale.ROOT).contains(" FROM "));
		assertFalse(json.toUpperCase(java.util.Locale.ROOT).contains("NPGSQL"));
	}

	private static SemanticCapability single(SemanticCapabilityContract contract, String id) {
		var matches = contract.capabilities().stream().filter(x -> x.id().equals(id)).toList();
		assertEquals(1, matches.size(), () -> "expected exactly one capability with id " + id);
		return matches.get(0);
	}
}
