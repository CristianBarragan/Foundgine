package com.foundgine.core.execution;

import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

class SecurityInvariantAttestationTest {

    @Test
    void satisfiedWhenNothingIsMissing() {
        SecurityInvariantAttestation attestation = SecurityInvariantAttestation.create(
                "postgres", List.of("a", "b"), List.of("b", "a"));

        assertTrue(attestation.isSatisfied());
        attestation.ensureSatisfied(); // should not throw
        assertEquals(List.of("a", "b"), attestation.required());
    }

    @Test
    void missingInvariantsAreReportedAndSortedOrdinal() {
        SecurityInvariantAttestation attestation = SecurityInvariantAttestation.create(
                "postgres", List.of("z", "a", "m"), List.of("a"));

        assertEquals(List.of("m", "z"), attestation.missing());
        assertThrows(IllegalStateException.class, attestation::ensureSatisfied);
    }

    @Test
    void hashIsDeterministicAndHex() {
        String h1 = ExecutionEvidenceFactory.hash("select 1");
        String h2 = ExecutionEvidenceFactory.hash("select 1");

        assertEquals(h1, h2);
        assertEquals(64, h1.length()); // SHA-256 -> 32 bytes -> 64 hex chars
    }
}
