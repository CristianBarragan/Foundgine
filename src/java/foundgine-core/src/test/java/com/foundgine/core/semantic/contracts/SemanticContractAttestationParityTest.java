package com.foundgine.core.semantic.contracts;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.SemanticContractAttestation;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticModelBuilder;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/** C# semantic contract attestation behavioral parity tests. */
class SemanticContractAttestationParityTest {
    private static SemanticModel model() {
        return new SemanticModelBuilder()
                .entity(new EntityId(1), "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "Name", String.class))
                .build();
    }

    @Test void matchingFingerprintAndSha256PrefixAreAccepted() {
        var model = model();
        assertTrue(SemanticContractAttestation.matches(model, model.contractFingerprint()));
        assertTrue(SemanticContractAttestation.matches(model, "sha256:" + model.contractFingerprint()));
    }

    @Test void mismatchedFingerprintIsRejected() {
        var model = model();
        assertFalse(SemanticContractAttestation.matches(model, "0".repeat(64)));
        var ex = assertThrows(IllegalStateException.class,
                () -> SemanticContractAttestation.ensureMatches(model, "0".repeat(64)));
        assertTrue(ex.getMessage().contains(model.contractFingerprint()));
    }
}
