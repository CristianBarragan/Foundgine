package com.foundgine.core.abstractions;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of {@code SemanticIdentityTests} / {@code SemanticIdentityJsonTests}
 * (Foundgine.Semantics.Tests), for the identity types ported so far.
 */
class SemanticIdentityTest {

	@Test
	void identityIsDeterministicForEachNamespace() {
		assertEquals(EntityId.create("Customer"), EntityId.create(" Customer "));
		assertEquals(FieldId.create("Customer", "Name"), FieldId.create("Customer", "Name"));
		assertEquals(RelationshipId.create("Customer", "Accounts"), RelationshipId.create("Customer", "Accounts"));
		assertEquals(ColumnId.create("public.customers", "name"), ColumnId.create("public.customers", "name"));
		assertEquals(ModelId.create("CustomerView"), ModelId.create("CustomerView"));
		assertEquals(ConnectionId.create("CustomerView", "Customer"), ConnectionId.create("CustomerView", "Customer"));
		assertEquals(AuthorizationId.create("CustomerPolicy", "CanRead"),
				AuthorizationId.create("CustomerPolicy", "CanRead"));
	}

	@Test
	void identityNamespacesDoNotShareCanonicalKeys() {
		assertNotEquals(SemanticIdentity.entityKey("Customer"), SemanticIdentity.tableKey("Customer"));
		assertNotEquals(SemanticIdentity.fieldKey("Customer", "Name"), SemanticIdentity.columnKey("Customer", "Name"));
	}

	@Test
	void zeroIsNeverReturnedByStableHash() {
		assertNotEquals(0L, SemanticIdentity.hash("test:anything"));
	}

	@Test
	void explicitZeroIdentityIsRejected() {
		IllegalArgumentException ex = assertThrows(IllegalArgumentException.class,
				() -> SemanticIdentity.validateExplicitId(0L, "field"));

		assertTrue(ex.getMessage().toLowerCase().contains("reserved"));
	}

	@Test
	void extendedIdentityNamespacesAreDistinct() {
		assertNotEquals(SemanticIdentity.modelKey("Orders"), SemanticIdentity.connectionKey("Orders", "Primary"));
		assertNotEquals(SemanticIdentity.connectionKey("Orders", "Primary"),
				SemanticIdentity.authorizationKey("Orders", "Primary"));
	}
}
