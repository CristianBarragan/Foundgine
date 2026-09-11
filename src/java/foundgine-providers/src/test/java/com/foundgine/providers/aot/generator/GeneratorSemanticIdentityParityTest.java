package com.foundgine.providers.aot.generator;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class GeneratorSemanticIdentityParityTest {
    @Test
    void declarationOrderDoesNotAffectCanonicalIdentity() {
        assertEquals(GeneratorSemanticIdentity.entityKey("Customer"),
                GeneratorSemanticIdentity.entityKey(" Customer "));
        assertEquals(GeneratorSemanticIdentity.fieldKey("Customer", "Name"),
                GeneratorSemanticIdentity.fieldKey(" Customer ", " Name "));
    }

    @Test
    void unrelatedModulesDoNotChangeExistingIdentity() {
        long customer = GeneratorSemanticIdentity.hash(GeneratorSemanticIdentity.entityKey("Customer"));
        long order = GeneratorSemanticIdentity.hash(GeneratorSemanticIdentity.entityKey("Order"));
        assertEquals(customer, GeneratorSemanticIdentity.hash("entity:Customer"));
        assertNotEquals(customer, order);
    }

    @Test
    void emptyIdentityComponentsFailClosed() {
        assertThrows(IllegalArgumentException.class, () -> GeneratorSemanticIdentity.entityKey(" "));
        assertThrows(IllegalArgumentException.class, () -> GeneratorSemanticIdentity.fieldKey("Customer", " "));
    }
}
