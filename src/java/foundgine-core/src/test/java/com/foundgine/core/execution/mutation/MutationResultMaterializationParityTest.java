package com.foundgine.core.execution.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.planning.mutation.*;
import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class MutationResultMaterializationParityTest {
    private static final EntityId CUSTOMER = EntityId.create("Customer");
    private static final EntityId ORDER = EntityId.create("Order");
    private static final FieldId CUSTOMER_ID = FieldId.create("Customer", "Id");
    private static final FieldId ORDER_ID = FieldId.create("Order", "Id");
    private static final RelationshipId ORDERS = RelationshipId.create("Customer", "orders");

    private SemanticModel model() {
        return new SemanticModelBuilder()
            .entity(CUSTOMER, "Customer", e -> e.identity(CUSTOMER_ID, "Id")
                .field(CUSTOMER_ID, "Id", Integer.class)
                .relationship(ORDERS, "orders", ORDER, RelationshipCardinality.MANY))
            .entity(ORDER, "Order", e -> e.identity(ORDER_ID, "Id").field(ORDER_ID, "Id", Integer.class))
            .build();
    }

    @Test
    void wrongOperationCountFailsClosed() {
        var intent = new NestedMutationIntent(
            new MutationIntent(CUSTOMER, MutationKind.CREATE, List.of()), List.of());
        var materializer = new MutationResultMaterializer(model());
        assertThrows(IllegalStateException.class, () -> materializer.materialize(intent, new MutationBatchResult(List.of())));
    }

    @Test
    void batchMaterializationRejectsUnaccountedResults() {
        var intent = new NestedMutationIntent(
            new MutationIntent(CUSTOMER, MutationKind.CREATE, List.of()), List.of());
        var item = new MutationResultMaterializer.Item("customer", intent);
        var result = new MutationBatchResult(List.of(new MutationResult(1, Map.of(CUSTOMER_ID, 1))));
        var materializer = new MutationResultMaterializer(model());
        assertEquals(1, materializer.materializeBatch(List.of(item), result).size());
        assertThrows(IllegalStateException.class, () -> materializer.materializeBatch(
            List.of(item), new MutationBatchResult(List.of(
                new MutationResult(1, Map.of(CUSTOMER_ID, 1)),
                new MutationResult(1, Map.of(ORDER_ID, 2))))));
    }
}
