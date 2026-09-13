package com.foundgine.samples.supplychain.advanced;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.resolution.*;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;
import java.util.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class SupplyChainGroundingAmbiguityParityTest {
    @Test void activeSupplierMeaningRequiresClarification() {
        var contract = SupplyChainSemanticModel.MODEL.createSnapshot();
        var source = source(
            new SemanticLexicalCandidate("active", SemanticLexicalCandidateKind.VALUE, "PurchaseOrder.Status = Open", .90,
                SupplyChainSemanticModel.PURCHASE_ORDER, null, SupplyChainSemanticModel.field("PurchaseOrder", "Status"), null, null, "Open", List.of()),
            new SemanticLexicalCandidate("active", SemanticLexicalCandidateKind.VALUE, "SupplierCertification.ValidTo >= today", .895,
                SupplyChainSemanticModel.CERTIFICATION, null, SupplyChainSemanticModel.field("SupplierCertification", "ValidTo"), null, null, "current", List.of()));
        var resolver = new SemanticLexicalResolver(contract, source);
        var decision = resolver.ground("active");
        assertEquals(GroundingOutcome.REQUIRES_CLARIFICATION, decision.outcome());
        assertNull(decision.committed());
        assertEquals(2, decision.competingInterpretations().size());
        assertEquals(SemanticLexicalResolutionOutcome.AMBIGUOUS, resolver.resolve("active").outcome());
    }

    @Test void duplicateEvidenceForSameRelationshipDoesNotCreateAmbiguity() {
        var relationship = SupplyChainSemanticModel.relationship("Supplier", "purchaseOrders");
        var source = source(
            new SemanticLexicalCandidate("supplied", SemanticLexicalCandidateKind.RELATIONSHIP, "purchaseOrders", .93,
                null, relationship, null, SupplyChainSemanticModel.SUPPLIER, SupplyChainSemanticModel.PURCHASE_ORDER, null, List.of()),
            new SemanticLexicalCandidate("supplied", SemanticLexicalCandidateKind.RELATIONSHIP, "purchaseOrders", .91,
                null, relationship, null, SupplyChainSemanticModel.SUPPLIER, SupplyChainSemanticModel.PURCHASE_ORDER, null, List.of()));
        var decision = new SemanticLexicalResolver(SupplyChainSemanticModel.MODEL.createSnapshot(), source).ground("supplied");
        assertEquals(GroundingOutcome.COMMITTED, decision.outcome());
        assertFalse(decision.hadCompetingMeanings());
        assertEquals(.93, decision.committed().steps().get(0).candidate().score(), 0.000001);
    }

    private ISemanticLexicalCandidateSource source(SemanticLexicalCandidate... candidates) {
        return request -> Arrays.stream(candidates).filter(x -> x.token().equalsIgnoreCase(request.token()))
            .filter(x -> request.effectiveKinds().contains(x.kind())).sorted(Comparator.comparingDouble(SemanticLexicalCandidate::score).reversed()).toList();
    }
}
