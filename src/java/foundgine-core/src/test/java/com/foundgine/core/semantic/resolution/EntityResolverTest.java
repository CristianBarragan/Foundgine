package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class EntityResolverTest {
    private static final EntityId CUSTOMER = EntityId.create("Customer");
    private static final EntityId ORDER = EntityId.create("Order");
    private static final FieldId CUSTOMER_ID = FieldId.create("Customer", "Id");
    private static final RelationshipId ORDERS = RelationshipId.create("Customer", "orders");

    private SemanticModel model() {
        return new SemanticModelBuilder()
            .entity(CUSTOMER, "Customer", e -> e.identity(CUSTOMER_ID, "Id").field(CUSTOMER_ID, "Id", String.class)
                .relationship(ORDERS, "orders", ORDER, RelationshipCardinality.MANY))
            .entity(ORDER, "Order", e -> e.identity("Id").field("Id".hashCode() == 0 ? FieldId.create("Order","Id") : FieldId.create("Order","Id"), "Id", String.class))
            .build();
    }

    @Test
    void explicitIdentityNeverInventsIdentity() {
        var source = new ICandidateSource() {
            public List<IdentityCandidate> findByIdentity(EntityId type, String value) {
                return type.equals(CUSTOMER) ? List.of(new IdentityCandidate(value, value)) : List.of();
            }
            public List<IdentityCandidate> findByRelationship(RelationshipId relationship, String source) { return List.of(); }
        };
        var result = new EntityResolver(model(), source).resolveByIdentity(CUSTOMER, "C-1");
        assertEquals(ResolutionOutcome.RESOLVED, result.outcome());
        assertEquals("C-1", result.resolved().identityValue());
        assertEquals(1.0, result.resolved().confidence());
    }

    @Test
    void multipleCandidatesAreAmbiguous() {
        var source = new ICandidateSource() {
            public List<IdentityCandidate> findByIdentity(EntityId type, String value) {
                return List.of(new IdentityCandidate("1","one"), new IdentityCandidate("2","two"));
            }
            public List<IdentityCandidate> findByRelationship(RelationshipId relationship, String source) { return List.of(); }
        };
        assertEquals(ResolutionOutcome.AMBIGUOUS,
            new EntityResolver(model(), source).resolveByIdentity(CUSTOMER, "C").outcome());
    }

    @Test
    void contractLexiconProjectsCanonicalNamesAndAliases() {
        var contract = new SemanticContractSnapshot(model().freeze());
        var source = new SemanticContractLexicalCandidateSource(contract);
        var candidates = source.retrieve(new SemanticLexicalRequest("customer"));
        assertFalse(candidates.isEmpty());
        assertTrue(candidates.stream().anyMatch(c -> c.kind() == SemanticLexicalCandidateKind.ENTITY));
    }
}
