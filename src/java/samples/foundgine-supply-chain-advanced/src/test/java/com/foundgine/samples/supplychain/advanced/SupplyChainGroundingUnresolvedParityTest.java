package com.foundgine.samples.supplychain.advanced;

import com.foundgine.core.semantic.resolution.*;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;
import java.util.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class SupplyChainGroundingUnresolvedParityTest {
    @Test void unknownBusinessThresholdTermFailsClosed() {
        var source = source(new SemanticLexicalCandidate("suppliers", SemanticLexicalCandidateKind.ENTITY, "Supplier", .95, SupplyChainSemanticModel.SUPPLIER, null, null, null, null, null, List.of()));
        var r = new SemanticLexicalResolver(SupplyChainSemanticModel.MODEL.createSnapshot(), source);
        var d = r.ground("risky suppliers");
        assertEquals(GroundingOutcome.UNRESOLVED, d.outcome());
        assertNull(d.committed());
        assertTrue(d.reason().contains("risky"));
        assertEquals(SemanticLexicalResolutionOutcome.UNRESOLVED, r.resolve("risky suppliers").outcome());
    }

    @Test void unsupportedRelativeDateTermFailsClosed() {
        var source = source(
            new SemanticLexicalCandidate("delayed", SemanticLexicalCandidateKind.VALUE, "Shipment.Status = Delayed", .93, SupplyChainSemanticModel.SHIPMENT, null, SupplyChainSemanticModel.field("Shipment", "Status"), null, null, "Delayed", List.of()),
            new SemanticLexicalCandidate("shipments", SemanticLexicalCandidateKind.ENTITY, "Shipment", .95, SupplyChainSemanticModel.SHIPMENT, null, null, null, null, null, List.of()));
        var d = new SemanticLexicalResolver(SupplyChainSemanticModel.MODEL.createSnapshot(), source).ground("delayed shipments last month");
        assertEquals(GroundingOutcome.UNRESOLVED, d.outcome());
        assertNull(d.committed());
        assertTrue(d.reason().contains("last"));
    }

    private ISemanticLexicalCandidateSource source(SemanticLexicalCandidate... cs) {
        return request -> Arrays.stream(cs).filter(x -> x.token().equalsIgnoreCase(request.token())).filter(x -> request.effectiveKinds().contains(x.kind())).sorted(Comparator.comparingDouble(SemanticLexicalCandidate::score).reversed()).toList();
    }
}
