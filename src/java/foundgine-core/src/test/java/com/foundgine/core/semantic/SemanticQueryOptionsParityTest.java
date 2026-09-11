package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

/** Mirrors Foundgine.Semantics.Tests/SemanticQueryOptionsTests.cs. */
class SemanticQueryOptionsParityTest {
    @Test
    void filterAndOrderAreProtocolNeutral() {
        var filter = new SemanticAndFilter(List.of(
                new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.EQ, "Alice"),
                new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.IN, new Object[]{1, 2})));

        var options = new SemanticQueryOptions(
                filter,
                List.of(new SemanticOrderTerm(new FieldId(2), SemanticSortDirection.DESC)),
                10, 20, null);

        assertInstanceOf(SemanticAndFilter.class, options.filter());
        assertEquals(SemanticSortDirection.DESC, options.effectiveOrder().get(0).direction());
        assertEquals(10, options.limit());
        assertEquals(20, options.offset());
    }

    @Test
    void relationshipFilterIsProtocolNeutral() {
        var filter = new SemanticRelationshipFilter(
                new com.foundgine.core.abstractions.RelationshipId(1),
                SemanticRelationshipQuantifier.SOME,
                new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.EQ, 100L));

        assertEquals(SemanticRelationshipQuantifier.SOME, filter.quantifier());
        assertEquals(new com.foundgine.core.abstractions.RelationshipId(1), filter.relationship());
    }
}
