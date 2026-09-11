package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

/** Mirrors the immutable contract boundary in SemanticModelFreezeTests.cs. */
class SemanticModelFreezeParityTest {
    @Test
    void builtModelIsNotFrozenUntilExplicitFreezeBoundary() {
        var model = buildModel();
        assertFalse(model.isFrozen());
        assertThrows(IllegalStateException.class, model::ensureFrozen);
    }

    @Test
    void freezePreservesContractIdentity() {
        var model = buildModel();
        var frozen = model.freeze();
        assertTrue(frozen.isFrozen());
        assertEquals(model.contractFingerprint(), frozen.contractFingerprint());
        assertEquals(model.entities().iterator().next().id(), frozen.entities().iterator().next().id());
    }

    @Test
    void freezingAlreadyFrozenModelIsIdempotent() {
        var frozen = buildModel().freeze();
        assertSame(frozen, frozen.freeze());
        frozen.ensureFrozen();
    }

    @Test
    void createSnapshotRequiresFrozenModel() {
        assertThrows(IllegalStateException.class, () -> buildModel().createSnapshot());
        assertNotNull(buildModel().freeze().createSnapshot());
    }

    private static SemanticModel buildModel() {
        return new SemanticModelBuilder()
                .entity(EntityId.create("Customer"), "Customer", e -> e
                        .identity(FieldId.create("Customer", "Id"), "Id")
                        .field(FieldId.create("Customer", "Name"), "Name", String.class))
                .build();
    }
}
