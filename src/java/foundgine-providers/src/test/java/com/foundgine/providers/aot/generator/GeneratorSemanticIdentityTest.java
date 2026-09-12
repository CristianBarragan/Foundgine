package com.foundgine.providers.aot.generator;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

class GeneratorSemanticIdentityTest {
	@Test
	void canonicalKeysMatchTheFoundgineIdentityContract() {
		assertEquals("entity:Order", GeneratorSemanticIdentity.entityKey(" Order "));
		assertEquals("field:Order.Id", GeneratorSemanticIdentity.fieldKey("Order", "Id"));
		assertEquals("relationship:Order.Customer", GeneratorSemanticIdentity.relationshipKey("Order", "Customer"));
		assertEquals("table:orders", GeneratorSemanticIdentity.tableKey("orders"));
		assertEquals("column:orders.id", GeneratorSemanticIdentity.columnKey("orders", "id"));
	}

	@Test
	void fnv1aHashIsStableAndZeroIsReserved() {
		assertEquals(7553954465381425432L, GeneratorSemanticIdentity.hash("entity:Order"));
		assertEquals(7908907399127070768L, GeneratorSemanticIdentity.hash("field:Order.Id"));
		assertNotEquals(0L, GeneratorSemanticIdentity.hash("anything"));
	}

	@Test
	void explicitZeroIsRejected() {
		assertThrows(IllegalArgumentException.class, () -> GeneratorSemanticIdentity.validateExplicitId(0L, "entity"));
		assertEquals(42L, GeneratorSemanticIdentity.validateExplicitId(42L, "entity"));
	}
}
