package com.foundgine.core.semantic.intent;

import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

class ReadIntentTypesTest {
    @Test void selectionsAndFiltersAreImmutable() {
        var selections = new java.util.ArrayList<ReadSelection>();
        selections.add(new ReadSelection("id"));
        var intent = new ReadIntent("Order", selections,
                new ReadFieldFilter("id", SemanticFilterOperator.EQ, "1"), List.of(), 10, 0, null, null);
        selections.clear();
        assertEquals(1, intent.selections().size());
        assertEquals("id", intent.selections().get(0).field());
    }

    @Test void intentDocumentRejectsWrongVersion() {
        var document = new SemanticIntentDocument("fingerprint", new ReadIntent("Order", List.of()), 99);
        assertThrows(IllegalArgumentException.class, document::validate);
    }
}
