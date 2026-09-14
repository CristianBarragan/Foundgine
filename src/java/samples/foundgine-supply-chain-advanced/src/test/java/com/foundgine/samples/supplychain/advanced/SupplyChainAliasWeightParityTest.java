package com.foundgine.samples.supplychain.advanced;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.AliasWeightEvidenceGate;
import com.foundgine.core.semantic.AliasWeightEvidenceGate.AliasEvidenceStatus;
import com.foundgine.core.semantic.AliasWeightEvidenceGate.ModelResolutionEvidence;
import com.foundgine.core.semantic.resolution.SemanticLexicalCandidate;
import com.foundgine.core.semantic.resolution.SemanticLexicalCandidateKind;
import com.foundgine.core.semantic.resolution.SemanticLexicalResolution;
import com.foundgine.core.semantic.resolution.SemanticLexicalResolutionOutcome;
import com.foundgine.core.semantic.resolution.SemanticLexicalStep;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;
import java.util.List;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Java parity for the C# advanced sample's
 * {@code Semantic/Tests/Grounding/SupplyChainAliasWeightTests.cs}.
 *
 * <p>Weights are evidence metadata, not retrieval scores, and never grant
 * authority. The gate is deliberately exercised separately from lexical
 * resolution so a low-confidence signal cannot silently become an
 * authorization decision.
 */
class SupplyChainAliasWeightParityTest {

    @Test
    void aotGeneratedSupplyChainContractPreservesWeightedEntityFieldAndRelationshipAliases() {
        var contract = SupplyChainSemanticModel.MODEL.createSnapshot();

        var supplier = contract.resolveEntity("Supplier");
        assertEquals(List.of("Vendor", "Seller"),
                supplier.effectiveAliases().stream().map(a -> a.name()).toList());
        assertEquals(List.of(95, 90),
                supplier.effectiveAliases().stream().map(a -> a.weight()).toList());

        var country = supplier.fields().stream().filter(f -> f.name().equals("Country")).findFirst().orElseThrow();
        assertEquals(List.of("State"), country.effectiveAliases().stream().map(a -> a.name()).toList());
        assertEquals(List.of(85), country.effectiveAliases().stream().map(a -> a.weight()).toList());

        var purchaseOrder = contract.resolveEntity("PurchaseOrder");
        assertEquals(List.of("PO", "POs", "Buy", "Buys"),
                purchaseOrder.effectiveAliases().stream().map(a -> a.name()).toList());
        assertEquals(List.of(100, 95, 90, 85),
                purchaseOrder.effectiveAliases().stream().map(a -> a.weight()).toList());

        var dueDate = purchaseOrder.fields().stream().filter(f -> f.name().equals("ExpectedArrival")).findFirst()
                .orElseThrow();
        assertEquals(List.of("DueDate"), dueDate.effectiveAliases().stream().map(a -> a.name()).toList());
        assertEquals(List.of(90), dueDate.effectiveAliases().stream().map(a -> a.weight()).toList());

        var supplierRel = purchaseOrder.relationships().stream().filter(r -> r.name().equals("supplier")).findFirst()
                .orElseThrow();
        assertEquals(List.of("vendor"), supplierRel.effectiveAliases().stream().map(a -> a.name()).toList());
        assertEquals(List.of(85), supplierRel.effectiveAliases().stream().map(a -> a.weight()).toList());
    }

    @Test
    void supplyChainWeightedAliasEvidenceOnlyCountsTheDeclaredLexicalIdentity() {
        var result = AliasWeightEvidenceGate.evaluate(SupplyChainSemanticModel.MODEL, 80,
                lexicalEntity("Vendor", SupplyChainSemanticModel.SUPPLIER), false);

        assertEquals(AliasEvidenceStatus.SUFFICIENT, result.status());
        assertTrue(result.isConclusive());
        assertTrue(result.violatingEntities().isEmpty());
        assertEquals(List.of(SupplyChainSemanticModel.SUPPLIER), List.copyOf(result.entityWeights().keySet()));
        assertEquals(95, result.entityWeights().get(SupplyChainSemanticModel.SUPPLIER));
    }

    @Test
    void supplyChainWeightedExampleCoversEntityFieldAndRelationshipAliases() {
        var supplier = SupplyChainSemanticModel.MODEL.get(SupplyChainSemanticModel.SUPPLIER);
        var purchaseOrder = SupplyChainSemanticModel.MODEL.get(SupplyChainSemanticModel.PURCHASE_ORDER);

        var weightedSupplierAliases = concatAliases(supplier);
        var weightedPurchaseOrderAliases = concatAliases(purchaseOrder);

        assertFalse(weightedSupplierAliases.isEmpty());
        assertFalse(weightedPurchaseOrderAliases.isEmpty());
        weightedSupplierAliases.forEach(a -> assertTrue(a.weight() >= 1 && a.weight() <= 100));
        weightedPurchaseOrderAliases.forEach(a -> assertTrue(a.weight() >= 1 && a.weight() <= 100));
    }

    @Test
    void loweringTheThresholdChangesOnlyTheEvidenceGateNotTheAliasIdentity() {
        var contract = SupplyChainSemanticModel.MODEL.createSnapshot();
        var supplier = contract.resolveEntity("Seller");

        var strict = AliasWeightEvidenceGate.evaluate(SupplyChainSemanticModel.MODEL, 100,
                lexicalEntity("Seller", SupplyChainSemanticModel.SUPPLIER), false);
        var relaxed = AliasWeightEvidenceGate.evaluate(SupplyChainSemanticModel.MODEL, 80,
                lexicalEntity("Seller", SupplyChainSemanticModel.SUPPLIER), false);

        assertEquals(AliasEvidenceStatus.INSUFFICIENT, strict.status());
        assertEquals(AliasEvidenceStatus.SUFFICIENT, relaxed.status());
        assertFalse(strict.isConclusive());
        assertTrue(relaxed.isConclusive());
        assertEquals(SupplyChainSemanticModel.SUPPLIER, supplier.id());
        assertTrue(supplier.effectiveAliases().stream().anyMatch(a -> a.name().equals("Seller") && a.weight() == 90));
    }

    @Test
    void fieldWeightDoesNotBecomeSupplierEntityWeight() {
        var field = SupplyChainSemanticModel.MODEL.get(SupplyChainSemanticModel.SUPPLIER).fields().stream()
                .filter(f -> f.name().equals("Country")).findFirst().orElseThrow();
        var result = AliasWeightEvidenceGate.evaluate(SupplyChainSemanticModel.MODEL, 90,
                lexicalField("State", SupplyChainSemanticModel.SUPPLIER, field.id()), false);

        assertFalse(result.isConclusive());
        assertTrue(result.entityWeights().isEmpty());
        assertEquals(85, result.fieldWeights().get(field.id()));
        assertTrue(result.violatingEntities().isEmpty());
    }

    @Test
    void certainModelIsADistinctProvenanceCategoryAndDoesNotInflateFieldEvidence() {
        var field = SupplyChainSemanticModel.MODEL.get(SupplyChainSemanticModel.SUPPLIER).fields().stream()
                .filter(f -> f.name().equals("Country")).findFirst().orElseThrow();
        var result = AliasWeightEvidenceGate.evaluate(SupplyChainSemanticModel.MODEL, 90,
                lexicalField("State", SupplyChainSemanticModel.SUPPLIER, field.id()), true);

        assertFalse(result.isConclusive());
        assertEquals(ModelResolutionEvidence.KNOWN_WITH_CERTAINTY, result.modelEvidence());
        // Compatibility projection only; not combined with field evidence below.
        assertEquals(100, result.modelWeight());
        assertEquals(85, result.fieldWeights().get(field.id()));
        assertTrue(result.entityWeights().isEmpty());
    }

    @Test
    void weightIsInertWhenTheRequestDidNotUseLexicalGrounding() {
        var result = AliasWeightEvidenceGate.evaluate(SupplyChainSemanticModel.MODEL, 100);

        assertEquals(AliasEvidenceStatus.NOT_APPLICABLE, result.status());
        assertTrue(result.isConclusive());
        assertNull(result.modelWeight());
        assertTrue(result.entityWeights().isEmpty());
        assertTrue(result.fieldWeights().isEmpty());
        assertTrue(result.relationshipWeights().isEmpty());
    }

    @Test
    void weightIsEvidenceOnlyAndDoesNotCreateASecondSemanticIdentity() {
        var contract = SupplyChainSemanticModel.MODEL.createSnapshot();
        var canonical = contract.resolveEntity("Supplier");
        var alias = contract.resolveEntity("seller");

        assertEquals(canonical.id(), alias.id());
        assertEquals(canonical.name(), alias.name());
    }

    private static List<com.foundgine.core.semantic.SemanticAlias> concatAliases(
            com.foundgine.core.semantic.SemanticEntity entity) {
        var all = new java.util.ArrayList<com.foundgine.core.semantic.SemanticAlias>();
        all.addAll(entity.effectiveAliases());
        entity.fields().forEach(f -> all.addAll(f.effectiveAliases()));
        entity.relationships().forEach(r -> all.addAll(r.effectiveAliases()));
        return all;
    }

    private static SemanticLexicalResolution lexicalField(String token, EntityId entityId, FieldId fieldId) {
        var candidate = new SemanticLexicalCandidate(token, SemanticLexicalCandidateKind.FIELD, token, .99, entityId,
                null, fieldId, null, null, null, List.of());
        return new SemanticLexicalResolution(SemanticLexicalResolutionOutcome.RESOLVED,
                List.of(new SemanticLexicalStep(token, candidate, .99, List.of())), .99, entityId, null);
    }

    private static SemanticLexicalResolution lexicalEntity(String token, EntityId entityId) {
        var candidate = new SemanticLexicalCandidate(token, SemanticLexicalCandidateKind.ENTITY, token, .99, entityId,
                null, null, null, null, null, List.of());
        return new SemanticLexicalResolution(SemanticLexicalResolutionOutcome.RESOLVED,
                List.of(new SemanticLexicalStep(token, candidate, .99, List.of())), .99, entityId, null);
    }
}