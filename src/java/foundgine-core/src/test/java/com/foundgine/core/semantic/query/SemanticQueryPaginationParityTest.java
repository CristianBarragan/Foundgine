package com.foundgine.core.semantic.query;

import com.foundgine.core.semantic.SemanticEntity;
import com.foundgine.core.semantic.SemanticFieldIdentity;
import com.foundgine.core.semantic.SemanticField;
import com.foundgine.core.semantic.SemanticRelationship;
import com.foundgine.core.abstractions.EntityId;
import org.junit.jupiter.api.Test;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

class SemanticQueryPaginationParityTest {
    private static SemanticEntity root() {
        return new SemanticEntity(new EntityId(1), "Customer", new SemanticFieldIdentity(new com.foundgine.core.abstractions.FieldId(1), "id"), List.<SemanticField>of(), List.<SemanticRelationship>of());
    }

    @Test void cursorRequiresPositiveLimit() {
        var options = new SemanticQueryOptions(null, List.of(), 0, null, "cursor-1");
        assertThrows(IllegalStateException.class, () -> SemanticQueryOptionsValidator.validate(options, root()));
    }

    @Test void cursorCannotBeCombinedWithOffset() {
        var options = new SemanticQueryOptions(null, List.of(), 10, 5, "cursor-1");
        assertThrows(IllegalStateException.class, () -> SemanticQueryOptionsValidator.validate(options, root()));
    }

    @Test void negativePaginationValuesAreRejected() {
        assertThrows(IllegalStateException.class, () -> SemanticQueryOptionsValidator.validate(
                new SemanticQueryOptions(null, List.of(), -1, null, null), root()));
        assertThrows(IllegalStateException.class, () -> SemanticQueryOptionsValidator.validate(
                new SemanticQueryOptions(null, List.of(), 10, -1, null), root()));
    }
}
