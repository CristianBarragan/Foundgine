package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class SemanticApproximateRetrievalParityTest {
    private static final EntityId PRODUCT = EntityId.create("Product");
    private static final FieldId NAME = FieldId.create("Product", "Name");

    private SemanticModel model() {
        return new SemanticModelBuilder()
            .entity(PRODUCT, "Product", e -> e.identity("Id").field(NAME, "Name", String.class))
            .build().freeze();
    }

    @Test void relationalRetrievalDoesNotInvokeApproximateSource() {
        var source = new IApproximateCandidateSource() {
            public List<RetrievalCandidate> retrieve(SemanticRetrievalRequest request) {
                fail("relational retrieval must not invoke approximate retrieval");
                return List.of();
            }
        };
        var result = new EntityResolver(model(), source).retrieve(
            new SemanticRetrievalRequest(PRODUCT, NAME, "widget", RetrievalStrategy.RELATIONAL));
        assertTrue(result.isEmpty());
    }

    @Test void approximateRetrievalIsEntityBoundedSortedAndLimited() {
        var source = (IApproximateCandidateSource) request -> List.of(
            new RetrievalCandidate(PRODUCT, "2", .70),
            new RetrievalCandidate(EntityId.create("Other"), "x", .99),
            new RetrievalCandidate(PRODUCT, "1", .90),
            new RetrievalCandidate(PRODUCT, "3", .80));
        var result = new EntityResolver(model(), source).retrieve(
            new SemanticRetrievalRequest(PRODUCT, NAME, "widget", RetrievalStrategy.SEARCH, 2));
        assertEquals(List.of("1", "3"), result.stream().map(RetrievalCandidate::recordId).toList());
    }

    @Test void retrievalRejectsInvalidLimits() {
        assertThrows(IllegalArgumentException.class,
            () -> new SemanticRetrievalRequest(PRODUCT, NAME, "widget", RetrievalStrategy.VECTOR, 0));
        assertThrows(IllegalArgumentException.class,
            () -> new SemanticRetrievalRequest(PRODUCT, NAME, "widget", RetrievalStrategy.VECTOR, 1001));
    }
}
