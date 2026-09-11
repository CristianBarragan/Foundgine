package com.foundgine.core.execution.e2e;

import com.foundgine.core.execution.ExecutionEvidenceFactory;
import com.foundgine.core.execution.SecurityInvariantAttestation;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

class ExecutionSecurityBoundaryParityE2ETest {
    @Test
    void missingSecurityInvariantsBlockExecution() {
        var attestation = SecurityInvariantAttestation.create(
                "postgres", List.of("authorization.required", "tenant.bound"), List.of("authorization.required"));
        assertFalse(attestation.isSatisfied());
        assertEquals(List.of("tenant.bound"), attestation.missing());
        assertThrows(IllegalStateException.class, attestation::ensureSatisfied);
    }

    @Test
    void executionEvidenceFingerprintIsDeterministic() {
        var first = ExecutionEvidenceFactory.hash("SELECT 1");
        var second = ExecutionEvidenceFactory.hash("SELECT 1");
        assertEquals(first, second);
        assertEquals(64, first.length());
    }
}
