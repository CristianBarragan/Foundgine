package com.foundgine.providers.storage.orm;

import static org.junit.jupiter.api.Assertions.*;

import java.math.BigDecimal;
import java.util.Arrays;
import java.util.List;
import org.junit.jupiter.api.Test;

class OrmAggregateCursorTest {
    @Test
    void encodes_null_rank_and_value_as_canonical_pairs() {
        String cursor = OrmAggregateCursor.encode(Arrays.asList(4L, null, new BigDecimal("12.50")));
        assertEquals(List.of(
                new OrmAggregateCursor.Slot(false, "4"),
                new OrmAggregateCursor.Slot(true, null),
                new OrmAggregateCursor.Slot(false, "12.50")),
                OrmAggregateCursor.decode(cursor, 3));
    }

    @Test
    void rejects_wrong_arity_and_inconsistent_null_rank() {
        assertThrows(IllegalStateException.class,
                () -> OrmAggregateCursor.decode(OrmAggregateCursor.encode(List.of(1L)), 2));
        assertThrows(IllegalStateException.class,
                () -> OrmAggregateCursor.decode(OrmCursor.encode(List.of("1", "not-null")), 1));
        assertThrows(IllegalStateException.class,
                () -> OrmAggregateCursor.decode(OrmCursor.encode(Arrays.asList("0", null)), 1));
        assertThrows(IllegalStateException.class,
                () -> OrmAggregateCursor.decode(OrmCursor.encode(List.of("x", "1")), 1));
    }

    @Test
    void keyset_comparison_obeys_nulls_last_and_direction() {
        assertTrue(OrmAggregateCursor.isAfter(Arrays.asList(3L, null), Arrays.asList(2L, "z"), List.of(false, false)));
        assertFalse(OrmAggregateCursor.isAfter(Arrays.asList(1L, null), Arrays.asList(2L, "z"), List.of(false, false)));
        assertTrue(OrmAggregateCursor.isAfter(List.of(1L, "a"), List.of(2L, "a"), List.of(true, false)));
        assertFalse(OrmAggregateCursor.isAfter(Arrays.asList(2L, null), Arrays.asList(2L, null), List.of(false, false)));
        assertTrue(OrmAggregateCursor.isAfter(Arrays.asList(2L, null), Arrays.asList(2L, "a"), List.of(false, true)));
    }

    @Test
    void typed_decode_coerces_values_and_preserves_null_slots() {
        String cursor = OrmAggregateCursor.encode(Arrays.asList("42", null, "12.50"));
        var values = OrmAggregateCursor.decodeTyped(cursor,
                List.of(Long.class, String.class, BigDecimal.class));
        assertEquals(Arrays.asList(42L, null, new BigDecimal("12.50")), values);
    }

    @Test
    void continuation_branches_encode_nulls_last_and_mixed_directions() {
        var branches = OrmAggregateCursor.continuationBranches(
                Arrays.asList(10L, null, "z"), List.of(false, true, true));
        assertEquals(2, branches.size());
        assertEquals(0, branches.get(0).keyIndex());
        assertTrue(branches.get(0).includesNull());
        assertEquals(OrmAggregateCursor.Comparison.GREATER_THAN, branches.get(0).comparison());
        assertEquals(Arrays.asList(10L, null), branches.get(1).equalPrefix());
        assertEquals(2, branches.get(1).keyIndex());
        assertEquals(OrmAggregateCursor.Comparison.LESS_THAN, branches.get(1).comparison());
        assertEquals(1, OrmAggregateCursor.continuationBranches(
                Arrays.asList(10L, null), List.of(false, false)).size());
        assertEquals(List.of(), OrmAggregateCursor.continuationBranches(
                Arrays.asList(10L, null), List.of(false, false)).get(0).equalPrefix());
    }

}
