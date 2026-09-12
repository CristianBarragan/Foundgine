package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class SemanticReferenceGroundingParityTest {
    private static final EntityId CUSTOMER = EntityId.create("Customer");
    private static final EntityId ORDER = EntityId.create("Order");
    private static final RelationshipId ORDERS = RelationshipId.create("Customer", "orders");

    private SemanticModel model() {
        return new SemanticModelBuilder()
            .entity(CUSTOMER, "Customer", e -> e.identity("Id").relationship(ORDERS, "orders", ORDER, RelationshipCardinality.MANY))
            .entity(ORDER, "Order", e -> e.identity("Id"))
            .build().freeze();
    }

    @Test void relationshipGroundingNeverInventsTargetIdentity() {
        var source = new ICandidateSource() {
            public List<IdentityCandidate> findByIdentity(EntityId type, String value) { return List.of(); }
            public List<IdentityCandidate> findByRelationship(RelationshipId relationship, String source) {
                return List.of(new IdentityCandidate("O-1", "Order one"));
            }
        };
        var customer = new ResolvedReference(CUSTOMER, "C-1", 1.0, "explicit", List.of());
        var result = new EntityResolver(model(), source).resolveByRelationship(customer, "orders");
        assertEquals(ResolutionOutcome.RESOLVED, result.outcome());
        assertEquals("O-1", result.resolved().identityValue());
    }

    @Test void missingTraversalRelationshipFailsClosed() {
        var source = new ICandidateSource() {
            public List<IdentityCandidate> findByIdentity(EntityId type, String value) { return List.of(); }
            public List<IdentityCandidate> findByRelationship(RelationshipId relationship, String source) { return List.of(); }
        };
        var customer = new ResolvedReference(CUSTOMER, "C-1", 1.0, "explicit", List.of());
        var result = new EntityResolver(model(), source).resolveByRelationship(customer, "does-not-exist");
        assertEquals(ResolutionOutcome.NOT_FOUND, result.outcome());
    }

    @Test void multipleRelationshipTargetsRemainAmbiguous() {
        var source = new ICandidateSource() {
            public List<IdentityCandidate> findByIdentity(EntityId type, String value) { return List.of(); }
            public List<IdentityCandidate> findByRelationship(RelationshipId relationship, String source) {
                return List.of(new IdentityCandidate("O-1", "one"), new IdentityCandidate("O-2", "two"));
            }
        };
        var customer = new ResolvedReference(CUSTOMER, "C-1", 1.0, "explicit", List.of());
        assertEquals(ResolutionOutcome.AMBIGUOUS,
            new EntityResolver(model(), source).resolveByRelationship(customer, "orders").outcome());
    }
}
