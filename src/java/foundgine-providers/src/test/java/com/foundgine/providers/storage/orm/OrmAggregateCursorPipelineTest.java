package com.foundgine.providers.storage.orm;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.ColumnId;
import java.math.BigDecimal;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

class OrmAggregateCursorPipelineTest {
    private static final FieldId ID = new FieldId(1L);

    @Test
    void emitsAndDecodesPrivateAggregateValuesPlusPrimaryKeyTieBreaker() {
        Map<FieldId, Object> row = new LinkedHashMap<>();
        row.put(ID, 42L);
        String cursor = OrmAggregateCursorPipeline.encode(
                "entity=Customer|auth=tenant:7|order=count(lines):desc,id:asc",
                Arrays.asList(3L, new BigDecimal("12.50")), List.of(ID), row);

        var decoded = OrmAggregateCursorPipeline.decode(cursor,
                "entity=Customer|auth=tenant:7|order=count(lines):desc,id:asc",
                List.of(Long.class, BigDecimal.class), List.of(Long.class));
        assertEquals(Arrays.asList(3L, new BigDecimal("12.50")), decoded.aggregateValues());
        assertEquals(List.of(42L), decoded.primaryKeyValues());
        assertEquals(Arrays.asList(3L, new BigDecimal("12.50"), 42L), decoded.orderedValues());
        assertEquals(3, OrmAggregateCursorPipeline.continuation(decoded, List.of(true, false)).size());
    }

    @Test
    void encodesInterleavedAggregateAndOrdinaryKeysInProviderOrderSequence() {
        var rootId = new FieldId(11L);
        var region = new FieldId(12L);
        var childId = new FieldId(13L);
        var root = new OrmEntityMapping(new EntityId(11L), "Root", Object.class, List.of(
                new OrmFieldBinding(rootId, "id", Long.class, true, new ColumnId(11L)),
                new OrmFieldBinding(region, "region", String.class, false, new ColumnId(12L))));
        var child = new OrmEntityMapping(new EntityId(12L), "Child", Object.class, List.of(
                new OrmFieldBinding(childId, "id", Long.class, true, new ColumnId(13L))));
        var count = new OrmOrder(childId, true, OrmOrder.AggregateKind.COUNT,
                child, new OrmLink(rootId, childId), new OrmPredicate.Const(true));
        var order = List.of(new OrmOrder(region, false), count, new OrmOrder(rootId, false));
        var row = new LinkedHashMap<FieldId, Object>();
        row.put(region, "north");
        row.put(rootId, 91L);

        String cursor = OrmAggregateCursorPipeline.encodeOrdered("plan", root, order,
                List.of(4L), row);
        var decoded = OrmAggregateCursorPipeline.decodeOrdered(cursor, "plan", root, order);
        assertEquals(List.of("north", 4L, 91L), decoded.orderedValues());
        assertEquals(3, OrmAggregateCursorPipeline.continuationOrdered(decoded, order).size());
    }

    @Test
    void createsFetchRequestWithDecodedPrivateContinuationTuple() {
        var rootId = new FieldId(21L);
        var childId = new FieldId(22L);
        var root = new OrmEntityMapping(new EntityId(21L), "Root", Object.class, List.of(
                new OrmFieldBinding(rootId, "id", Long.class, true, new ColumnId(21L))));
        var child = new OrmEntityMapping(new EntityId(22L), "Child", Object.class, List.of(
                new OrmFieldBinding(childId, "id", Long.class, true, new ColumnId(22L))));
        var aggregate = new OrmOrder(childId, true, OrmOrder.AggregateKind.COUNT,
                child, new OrmLink(rootId, childId), new OrmPredicate.Const(true));
        var order = List.of(aggregate, new OrmOrder(rootId, false));
        var row = Map.<FieldId, Object>of(rootId, 73L);
        String token = OrmAggregateCursorPipeline.nextPageCursor("trusted-plan-v1", root,
                order, List.of(5L), row);

        OrmFetchRequest request = OrmAggregateCursorPipeline.continuationRequest(token,
                "trusted-plan-v1", root, List.of(rootId), List.of(new OrmPredicate.Const(true)),
                order, 10);
        assertEquals(List.of(5L, 73L), request.continuationValues());
        assertEquals(order, request.order());
        assertEquals(10, request.limit());
        assertEquals(null, request.offset());
    }

    @Test
    void rejectsAggregateContinuationWhenPlanIdentityChanges() {
        var rootId = new FieldId(31L);
        var childId = new FieldId(32L);
        var root = new OrmEntityMapping(new EntityId(31L), "Root", Object.class, List.of(
                new OrmFieldBinding(rootId, "id", Long.class, true, new ColumnId(31L))));
        var child = new OrmEntityMapping(new EntityId(32L), "Child", Object.class, List.of(
                new OrmFieldBinding(childId, "id", Long.class, true, new ColumnId(32L))));
        var order = List.of(new OrmOrder(childId, false, OrmOrder.AggregateKind.COUNT,
                child, new OrmLink(rootId, childId), new OrmPredicate.Const(true)),
                new OrmOrder(rootId, false));
        String token = OrmAggregateCursorPipeline.nextPageCursor("tenant=7", root,
                order, List.of(2L), Map.of(rootId, 1L));
        assertThrows(IllegalArgumentException.class, () ->
                OrmAggregateCursorPipeline.continuationRequest(token, "tenant=8", root,
                        List.of(rootId), List.of(new OrmPredicate.Const(true)), order, 5));
    }

    @Test
    void preservesNullAggregateAndRejectsNullPrimaryKey() {
        Map<FieldId, Object> row = Map.of(ID, 7L);
        String cursor = OrmAggregateCursorPipeline.encode("plan", Arrays.asList((Object) null), List.of(ID), row);
        var decoded = OrmAggregateCursorPipeline.decode(cursor, "plan", List.of(Long.class), List.of(Long.class));
        assertEquals(Arrays.asList((Object) null), decoded.aggregateValues());
        assertThrows(IllegalArgumentException.class,
                () -> OrmAggregateCursorPipeline.encode("plan", List.of(1L), List.of(ID), new java.util.HashMap<>() {{ put(ID, null); }}));
    }

    @Test
    void rejectsCursorReplayForDifferentPlanAndDirectionArity() {
        Map<FieldId, Object> row = Map.of(ID, 7L);
        String cursor = OrmAggregateCursorPipeline.encode("plan-a", List.of(1L), List.of(ID), row);
        assertThrows(IllegalArgumentException.class,
                () -> OrmAggregateCursorPipeline.decode(cursor, "plan-b", List.of(Long.class), List.of(Long.class)));
        var decoded = OrmAggregateCursorPipeline.decode(cursor, "plan-a", List.of(Long.class), List.of(Long.class));
        assertThrows(IllegalArgumentException.class,
                () -> OrmAggregateCursorPipeline.continuation(decoded, List.of()));
    }
}
