package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.AliasWeightEvidenceGate;
import com.foundgine.core.semantic.SemanticAlias;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.RelationshipCardinality;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

/** Mirrors Foundgine.Semantics.Tests/AliasWeightEvidenceGateTests.cs. */
class AliasWeightEvidenceGateParityTest {
    private static SemanticModel model(java.util.function.Consumer<com.foundgine.core.semantic.SemanticEntityBuilder> c) {
        return new SemanticModelBuilder().entity(EntityId.create("Supplier"), "Supplier", e -> {
            e.identity(FieldId.create("Supplier", "Id"), "Id");
            e.field(FieldId.create("Supplier", "State"), "State", String.class);
            c.accept(e);
        }).build();
    }
    private static SemanticLexicalResolution entityResolution(String token, EntityId id, double score) {
        return new SemanticLexicalResolution(SemanticLexicalResolutionOutcome.RESOLVED,
            List.of(new SemanticLexicalStep(token, new SemanticLexicalCandidate(token, SemanticLexicalCandidateKind.ENTITY,
                "Supplier", score, id, null, null, null, null, null, null, List.of()), score, List.of())), score, id, null);
    }
    private static SemanticLexicalResolution fieldResolution(String token, EntityId entity, FieldId field, double score) {
        return new SemanticLexicalResolution(SemanticLexicalResolutionOutcome.RESOLVED,
            List.of(new SemanticLexicalStep(token, new SemanticLexicalCandidate(token, SemanticLexicalCandidateKind.FIELD,
                "State", score, entity, null, field, null, null, null, null, List.of()), score, List.of())), score, entity, null);
    }
    @Test void weightIsInertWithoutLexicalGrounding() {
        var m = model(e -> e.alias("Vendor", 50));
        var r = AliasWeightEvidenceGate.evaluate(m, 90);
        assertEquals(AliasWeightEvidenceGate.AliasEvidenceStatus.NOT_APPLICABLE, r.status());
        assertTrue(r.isConclusive()); assertEquals(AliasWeightEvidenceGate.ModelResolutionEvidence.UNKNOWN, r.modelEvidence());
        assertNull(r.modelWeight()); assertTrue(r.entityWeights().isEmpty()); assertTrue(r.fieldWeights().isEmpty());
        assertFalse(r.contractFingerprint().isEmpty());
    }
    @Test void knownModelIsDistinctProvenance() {
        var m = model(e -> e.alias("Vendor", 50));
        var r = AliasWeightEvidenceGate.evaluate(m, 90, entityResolution("Vendor", new EntityId(1), .91), true);
        assertEquals(AliasWeightEvidenceGate.AliasEvidenceStatus.INSUFFICIENT, r.status());
        assertFalse(r.isConclusive()); assertEquals(AliasWeightEvidenceGate.ModelResolutionEvidence.KNOWN_WITH_CERTAINTY, r.modelEvidence());
        assertEquals(100, r.modelWeight()); assertEquals(50, r.entityWeights().get(new EntityId(1)));
        assertTrue(r.violatingEntities().contains(new EntityId(1)));
    }
    @Test void fieldWeightNeverBecomesEntityWeight() {
        var entity = new EntityId(1); var field = FieldId.create("Supplier", "State");
        var m = model(e -> e.fieldAlias(field, "State", 50));
        var r = AliasWeightEvidenceGate.evaluate(m, 80, fieldResolution("State", entity, field, .95), false);
        assertFalse(r.isConclusive()); assertTrue(r.entityWeights().isEmpty()); assertEquals(50, r.fieldWeights().get(field));
        assertTrue(r.violatingEntities().isEmpty()); assertEquals(List.of(field), r.violatingFields());
    }
    @Test void entityWeightIsScopedToEntity() {
        var id = new EntityId(1); var r = AliasWeightEvidenceGate.evaluate(model(e -> e.alias("Vendor", 90)), 80, entityResolution("Vendor", id, .95), false);
        assertEquals(AliasWeightEvidenceGate.AliasEvidenceStatus.SUFFICIENT, r.status()); assertTrue(r.isConclusive()); assertEquals(90, r.entityWeights().get(id));
    }
    @Test void relationshipWeightIsScopedToRelationship() {
        var rid = new RelationshipId(12); var m = new SemanticModelBuilder()
            .entity(EntityId.create("Supplier"), "Supplier", e -> { e.identity(FieldId.create("Supplier","Id"),"Id"); e.relationship(rid,"Orders",new EntityId(2),RelationshipCardinality.MANY); e.relationshipAlias(rid,"orders",60); })
            .entity(new EntityId(2), "Order", e -> e.identity(FieldId.create("Order","Id"),"Id")).build();
        var c = new SemanticLexicalCandidate("orders", SemanticLexicalCandidateKind.RELATIONSHIP, "Orders", .9, null, rid, null, new EntityId(1), new EntityId(2), null, List.of());
        var rsl = new SemanticLexicalResolution(SemanticLexicalResolutionOutcome.RESOLVED,List.of(new SemanticLexicalStep("orders",c,.9,List.of())),.9,new EntityId(1),null);
        var r = AliasWeightEvidenceGate.evaluate(m,80,rsl,false);
        assertFalse(r.isConclusive()); assertTrue(r.entityWeights().isEmpty()); assertEquals(60,r.relationshipWeights().get(rid)); assertEquals(List.of(rid),r.violatingRelationships());
    }
    @ParameterizedTest @ValueSource(ints={1,100}) void aliasWeightAcceptsBoundaries(int weight){ assertEquals(weight,new SemanticAlias("Vendor",weight).weight()); }
    @ParameterizedTest @ValueSource(ints={0,101}) void aliasWeightRejectsOutsideRange(int weight){ assertThrows(IllegalArgumentException.class,()->new SemanticAlias("Vendor",weight)); }
    @Test void strongestEvidenceWinsForSameIdentity() {
        var id=new EntityId(1); var m=model(e->e.alias("Vendor",95).alias("Seller",40));
        var steps=List.of(
            new SemanticLexicalStep("Vendor",new SemanticLexicalCandidate("Vendor",SemanticLexicalCandidateKind.ENTITY,"Supplier",.81,id,null,null,null,null,null,null,List.of()),.81,List.of()),
            new SemanticLexicalStep("Seller",new SemanticLexicalCandidate("Seller",SemanticLexicalCandidateKind.ENTITY,"Supplier",.97,id,null,null,null,null,null,null,List.of()),.97,List.of()));
        var r=AliasWeightEvidenceGate.evaluate(m,90,new SemanticLexicalResolution(SemanticLexicalResolutionOutcome.RESOLVED,steps,.89,id,null),false);
        assertEquals(AliasWeightEvidenceGate.AliasEvidenceStatus.SUFFICIENT,r.status()); assertEquals(95,r.entityWeights().get(id));
    }
    @Test void unweightedAliasProducesNoEvidence() {
        var r=AliasWeightEvidenceGate.evaluate(model(e->e.alias("Vendor")),1,entityResolution("Vendor",new EntityId(1),.95),false);
        assertEquals(AliasWeightEvidenceGate.AliasEvidenceStatus.NOT_APPLICABLE,r.status()); assertTrue(r.entityWeights().isEmpty()); assertTrue(r.violatingEntities().isEmpty());
    }
    @Test void weightedAndUnweightedOnlyWeightedContributes() {
        var id=new EntityId(1); var m=model(e->e.alias("Vendor").alias("Seller",60));
        var steps=List.of(new SemanticLexicalStep("Vendor",new SemanticLexicalCandidate("Vendor",SemanticLexicalCandidateKind.ENTITY,"Supplier",.81,id,null,null,null,null,null,null,List.of()),.81,List.of()),new SemanticLexicalStep("Seller",new SemanticLexicalCandidate("Seller",SemanticLexicalCandidateKind.ENTITY,"Supplier",.70,id,null,null,null,null,null,null,List.of()),.70,List.of()));
        var r=AliasWeightEvidenceGate.evaluate(m,50,new SemanticLexicalResolution(SemanticLexicalResolutionOutcome.RESOLVED,steps,.75,id,null),false);
        assertEquals(AliasWeightEvidenceGate.AliasEvidenceStatus.SUFFICIENT,r.status()); assertEquals(60,r.entityWeights().get(id));
    }
    @Test void canonicalNameDoesNotContributeAliasEvidence() {
        var id=new EntityId(1); var m=model(e->e.alias("Vendor",70));
        var steps=List.of(new SemanticLexicalStep("Supplier",new SemanticLexicalCandidate("Supplier",SemanticLexicalCandidateKind.ENTITY,"Supplier",.99,id,null,null,null,null,null,null,List.of()),.99,List.of()),new SemanticLexicalStep("Vendor",new SemanticLexicalCandidate("Vendor",SemanticLexicalCandidateKind.ENTITY,"Supplier",.81,id,null,null,null,null,null,null,List.of()),.81,List.of()));
        var r=AliasWeightEvidenceGate.evaluate(m,60,new SemanticLexicalResolution(SemanticLexicalResolutionOutcome.RESOLVED,steps,.90,id,null),false);
        assertEquals(AliasWeightEvidenceGate.AliasEvidenceStatus.SUFFICIENT,r.status()); assertEquals(70,r.entityWeights().get(id));
    }
    @Test void fingerprintIdentifiesFrozenContract() {
        var a=model(e->e.alias("Vendor",90)); var b=model(e->e.alias("Vendor",40)); var id=new EntityId(1);
        var a1=AliasWeightEvidenceGate.evaluate(a,80,entityResolution("Vendor",id,.9),false); var a2=AliasWeightEvidenceGate.evaluate(a,80,entityResolution("Vendor",id,.9),false); var br=AliasWeightEvidenceGate.evaluate(b,80,entityResolution("Vendor",id,.9),false);
        assertFalse(a1.contractFingerprint().isEmpty()); assertEquals(a1.contractFingerprint(),a2.contractFingerprint()); assertNotEquals(a1.contractFingerprint(),br.contractFingerprint());
    }
}
