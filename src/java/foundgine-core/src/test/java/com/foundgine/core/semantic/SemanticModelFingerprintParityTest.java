package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

/** Mirrors the contract-fingerprint portion of SemanticModelFingerprintTests.cs. */
class SemanticModelFingerprintParityTest {
    @Test
    void fingerprintIsStableWhenDeclarationsAreReordered() {
        var first = buildModel(false);
        var second = buildModel(true);
        assertEquals(first.contractFingerprint(), second.contractFingerprint());
    }

    @Test
    void fingerprintChangesWhenSemanticContractChanges() {
        var first = buildModel(false);
        var changed = new SemanticModelBuilder()
                .entity(EntityId.create("Product"), "Product", e -> e
                        .identity(FieldId.create("Product", "Id"), "Id")
                        .field(FieldId.create("Product", "Name"), "Name", String.class)
                        .field(FieldId.create("Product", "Price"), "Price", Double.class)
                        .field(FieldId.create("Product", "Sku"), "Sku", String.class))
                .build();
        assertNotEquals(first.contractFingerprint(), changed.contractFingerprint());
    }

    @Test
    void fingerprintChangesWhenAliasChanges() {
        var first = new SemanticModelBuilder()
                .entity(EntityId.create("Product"), "Product", e -> e
                        .identity(FieldId.create("Product", "Id"), "Id")
                        .field(FieldId.create("Product", "Name"), "Name", String.class)
                        .fieldAlias(FieldId.create("Product", "Name"), "DisplayName"))
                .build();
        var changed = new SemanticModelBuilder()
                .entity(EntityId.create("Product"), "Product", e -> e
                        .identity(FieldId.create("Product", "Id"), "Id")
                        .field(FieldId.create("Product", "Name"), "Name", String.class)
                        .fieldAlias(FieldId.create("Product", "Name"), "ProductName"))
                .build();
        assertNotEquals(first.contractFingerprint(), changed.contractFingerprint());
    }

    @Test
    void fingerprintIsLowercaseSha256() {
        var fingerprint = buildModel(false).contractFingerprint();
        assertEquals(64, fingerprint.length());
        assertTrue(fingerprint.matches("^[0-9a-f]{64}$"));
    }

    private static SemanticModel buildModel(boolean reordered) {
        return new SemanticModelBuilder()
                .entity(EntityId.create("Product"), "Product", e -> {
                    e.identity(FieldId.create("Product", "Id"), "Id");
                    if (reordered) {
                        e.field(FieldId.create("Product", "Price"), "Price", Double.class);
                        e.field(FieldId.create("Product", "Name"), "Name", String.class);
                    } else {
                        e.field(FieldId.create("Product", "Name"), "Name", String.class);
                        e.field(FieldId.create("Product", "Price"), "Price", Double.class);
                    }
                }).build();
    }
}