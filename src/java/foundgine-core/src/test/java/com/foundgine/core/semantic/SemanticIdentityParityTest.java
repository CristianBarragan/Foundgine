package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.SemanticIdentity;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/** C# SemanticIdentity behavioral parity tests. */
class SemanticIdentityParityTest {
    @Test void identityNamespacesAreDeterministicAndDistinct() {
        assertEquals(SemanticIdentity.entityKey("Customer"), SemanticIdentity.entityKey(" Customer "));
        assertEquals(SemanticIdentity.fieldKey("Customer", "Name"), SemanticIdentity.fieldKey("Customer", "Name"));
        assertEquals(SemanticIdentity.relationshipKey("Customer", "Accounts"), SemanticIdentity.relationshipKey("Customer", "Accounts"));
        assertNotEquals(SemanticIdentity.entityKey("Customer"), SemanticIdentity.tableKey("Customer"));
        assertNotEquals(SemanticIdentity.fieldKey("Customer", "Name"), SemanticIdentity.columnKey("Customer", "Name"));
    }

    @Test void stableHashNeverReturnsReservedZero() {
        assertNotEquals(0L, SemanticIdentity.hash("test:anything"));
    }

    @Test void explicitZeroIdentityIsRejected() {
        var ex = assertThrows(IllegalArgumentException.class,
                () -> SemanticIdentity.validateExplicitId(0L, "field"));
        assertTrue(ex.getMessage().toLowerCase().contains("reserved"));
    }
}
