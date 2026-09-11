package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import org.junit.jupiter.api.Test;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

class SecurityPreservationParityTest {
    private static SemanticPlan plan(List<String> invariants) {
        return new SemanticPlan(new SemanticPlanNode(1, ExecutionOperation.SCAN, new EntityId(1),
                List.of(), null, null, List.of()), invariants, null);
    }

    @Test void invariantOrderDoesNotChangeSecurityProof() {
        var a = plan(List.of(SecurityInvariantIds.TENANT_ISOLATION, SecurityInvariantIds.RUNTIME_AUTHORIZATION));
        var b = plan(List.of(SecurityInvariantIds.RUNTIME_AUTHORIZATION, SecurityInvariantIds.TENANT_ISOLATION));
        var proof = SecurityPreservationProof.create(a, b);
        assertTrue(proof.isSatisfied());
        assertTrue(proof.missing().isEmpty());
    }

    @Test void unknownInvariantFailsClosed() {
        var source = plan(List.of("security.unknown"));
        var rewritten = plan(List.of());
        assertThrows(IllegalStateException.class, () -> SecurityPreservationProof.create(source, rewritten));
    }
}
