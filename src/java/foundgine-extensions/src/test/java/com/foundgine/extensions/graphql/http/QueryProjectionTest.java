package com.foundgine.extensions.graphql.http;

import static org.junit.jupiter.api.Assertions.assertEquals;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.execution.ExecutionCellKey;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.extensions.graphql.GraphQLResultShape;
import com.foundgine.extensions.graphql.GraphQLResultShape.GraphQLResultField;
import com.foundgine.extensions.graphql.http.GraphQLHttpHandler.QueryProjection;
import com.foundgine.extensions.graphql.http.GraphQLHttpHandler.QueryProjection.Child;
import com.foundgine.extensions.graphql.http.GraphQLHttpHandler.QueryProjection.Node;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

/** Drives the flat-rows -> nested-GraphQL assembly directly, without a model or a database. */
class QueryProjectionTest {
    private static final EntityId ORDER = EntityId.create("Order");
    private static final EntityId LINE = EntityId.create("OrderLine");
    private static final FieldId ORDER_ID = FieldId.create("Order", "Id");
    private static final FieldId ORDER_TOTAL = FieldId.create("Order", "Total");
    private static final FieldId LINE_ID = FieldId.create("OrderLine", "Id");
    private static final FieldId LINE_QTY = FieldId.create("OrderLine", "Quantity");

    private static Node tree() {
        var lineShape = new GraphQLResultShape(List.of(
                new GraphQLResultField(LINE_ID, null, "id", "id"),
                new GraphQLResultField(LINE_QTY, null, "quantity", "quantity")));
        var lineNode = new Node(1, LINE, LINE_ID, lineShape, List.of());
        var linesField = new GraphQLResultField(null, RelationshipId.create("Order", "Lines"), "lines", "lines", lineShape);
        var orderShape = new GraphQLResultShape(List.of(
                new GraphQLResultField(ORDER_ID, null, "id", "id"),
                new GraphQLResultField(ORDER_TOTAL, null, "total", "total"),
                linesField));
        return new Node(0, ORDER, ORDER_ID, orderShape, List.of(new Child(linesField, lineNode, true)));
    }

    private static ExecutionRow row(Object orderId, Object total, Object lineId, Object qty) {
        Map<ExecutionCellKey, Object> cells = new HashMap<>();
        cells.put(new ExecutionCellKey(0, ORDER, ORDER_ID), orderId);
        cells.put(new ExecutionCellKey(0, ORDER, ORDER_TOTAL), total);
        if (lineId != null) {
            cells.put(new ExecutionCellKey(1, LINE, LINE_ID), lineId);
            cells.put(new ExecutionCellKey(1, LINE, LINE_QTY), qty);
        }
        return new ExecutionRow(Map.of(), cells);
    }

    @Test
    void groupsRepeatedParentRowsAndCollectsDistinctChildren() {
        var rows = List.of(row(1, 45, 10, 2), row(1, 45, 11, 1), row(1, 45, 11, 1), row(2, 150, null, null));
        var result = QueryProjection.materialize(rows, tree());
        assertEquals(List.of(
                Map.of("id", 1, "total", 45, "lines", List.of(Map.of("id", 10, "quantity", 2), Map.of("id", 11, "quantity", 1))),
                Map.of("id", 2, "total", 150, "lines", List.of())), result);
    }

    @Test
    void identityTextIsStableAcrossNumberRepresentations() {
        var rows = List.of(row(1, 1, null, null), row(1L, 1, null, null));
        assertEquals(1, QueryProjection.materialize(rows, tree()).size());
    }
}
