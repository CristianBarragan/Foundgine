package com.foundgine.samples.supplychain.advanced;

import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.resolution.*;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;
import java.util.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

/** Content-level parity for the Advanced C# grounding-alias suite. */
class SupplyChainGroundingAliasParityTest {
    private SemanticContractSnapshot contract() { return SupplyChainSemanticModel.MODEL.createSnapshot(); }

    @Test void domainAliasesAreProjectedIntoTheRealLexicon() {
        var lexicon = SemanticLexiconProjection.build(contract());
        var supplier = lexicon.stream().filter(x -> x.kind() == SemanticLexicalCandidateKind.ENTITY && x.canonicalName().equals("Supplier")).findFirst().orElseThrow();
        var po = lexicon.stream().filter(x -> x.kind() == SemanticLexicalCandidateKind.ENTITY && x.canonicalName().equals("PurchaseOrder")).findFirst().orElseThrow();
        assertTrue(supplier.effectiveAliases().contains("Vendor"));
        assertTrue(supplier.effectiveAliases().contains("Seller"));
        assertTrue(po.effectiveAliases().contains("PO"));
        assertTrue(po.effectiveAliases().contains("Buys"));
        var supplierModel = SupplyChainSemanticModel.MODEL.get(SupplyChainSemanticModel.SUPPLIER);
        assertEquals(95, supplierModel.effectiveAliases().stream().filter(a -> a.name().equals("Vendor")).findFirst().orElseThrow().weight());
        assertEquals(90, supplierModel.effectiveAliases().stream().filter(a -> a.name().equals("Seller")).findFirst().orElseThrow().weight());
        var country = supplierModel.fields().stream().filter(f -> f.name().equals("Country")).findFirst().orElseThrow();
        assertEquals(85, country.effectiveAliases().stream().filter(a -> a.name().equals("State")).findFirst().orElseThrow().weight());
        var poModel = SupplyChainSemanticModel.MODEL.get(SupplyChainSemanticModel.PURCHASE_ORDER);
        assertEquals(100, poModel.effectiveAliases().stream().filter(a -> a.name().equals("PO")).findFirst().orElseThrow().weight());
        var due = poModel.fields().stream().filter(f -> f.name().equals("ExpectedArrival")).findFirst().orElseThrow();
        assertEquals(90, due.effectiveAliases().stream().filter(a -> a.name().equals("DueDate")).findFirst().orElseThrow().weight());
    }

    @Test void retrievalRepresentationsCollapseToOneSemanticIdentity() {
        var resolver = resolver();
        var seller = resolver.getCandidates("seller").get("seller");
        var buys = resolver.getCandidates("buys").get("buys");
        assertEquals(1, seller.size());
        assertEquals(SemanticLexicalCandidateKind.ENTITY, seller.get(0).kind());
        assertEquals(SupplyChainSemanticModel.SUPPLIER, seller.get(0).entityId());
        assertEquals(1, buys.size());
        assertEquals(SemanticLexicalCandidateKind.ENTITY, buys.get(0).kind());
        assertEquals(SupplyChainSemanticModel.PURCHASE_ORDER, buys.get(0).entityId());
    }

    @Test void canonicalNamesWinOverSameNamedRelationshipRoots() {
        var supplier = resolver().ground("Supplier");
        var po = resolver().ground("PurchaseOrder");
        assertEquals(GroundingOutcome.COMMITTED, supplier.outcome());
        assertEquals(GroundingOutcome.COMMITTED, po.outcome());
        assertEquals(SupplyChainSemanticModel.SUPPLIER, supplier.committed().rootEntity());
        assertEquals(SupplyChainSemanticModel.PURCHASE_ORDER, po.committed().rootEntity());
        assertEquals("Supplier", supplier.committed().steps().get(0).candidate().canonicalName());
        assertEquals("PurchaseOrder", po.committed().steps().get(0).candidate().canonicalName());
    }

    @Test void sellerAndBuysGroundToCanonicalIdentities() {
        var r = resolver();
        var supplier = r.ground("Supplier");
        var seller = r.ground("seller");
        var po = r.ground("PurchaseOrder");
        var buys = r.ground("buys");
        assertEquals(GroundingOutcome.COMMITTED, seller.outcome());
        assertEquals(GroundingOutcome.COMMITTED, buys.outcome());
        assertEquals(SupplyChainSemanticModel.SUPPLIER, seller.committed().rootEntity());
        assertEquals(SupplyChainSemanticModel.PURCHASE_ORDER, buys.committed().rootEntity());
        assertEquals(supplier.committed().signature(), seller.committed().signature());
        assertEquals(po.committed().signature(), buys.committed().signature());
    }

    private SemanticLexicalResolver resolver() {
        var contract = contract();
        var lexicon = SemanticLexiconProjection.build(contract);
        ISemanticLexicalCandidateSource source = request -> lexicon.stream()
            .filter(e -> request.effectiveKinds().contains(e.kind()))
            .filter(e -> e.canonicalName().equalsIgnoreCase(request.token()) || e.effectiveAliases().stream().anyMatch(a -> a.equalsIgnoreCase(request.token())))
            .map(e -> new SemanticLexicalCandidate(request.token(), e.kind(), e.canonicalName(), .95, e.entityId(), e.relationshipId(), e.fieldId(), e.sourceEntityId(), e.targetEntityId(), e.value(), List.of()))
            .toList();
        return new SemanticLexicalResolver(contract, source);
    }
}
