package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/** C# SemanticRelationshipFilter behavioral parity tests. */
class SemanticRelationshipFilterParityTest {
    @Test void relationshipFilterRemainsProtocolNeutral() {
        var filter = new SemanticRelationshipFilter(
                new RelationshipId(1),
                SemanticRelationshipQuantifier.SOME,
                new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.EQ, 100));

        assertEquals(SemanticRelationshipQuantifier.SOME, filter.quantifier());
        assertEquals(new RelationshipId(1), filter.relationship());
    }
}
