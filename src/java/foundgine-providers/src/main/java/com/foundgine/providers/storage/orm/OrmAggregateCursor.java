package com.foundgine.providers.storage.orm;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/**
 * Canonical private cursor slots for aggregate ordering. Each logical key is encoded as a
 * null-rank followed by its value: rank 0 means non-null, rank 1 means null. This makes NULLS
 * LAST independent of sort direction and database defaults. The value slot is null exactly
 * when rank is 1. These slots are transport metadata and must never be projected to callers.
 */
public final class OrmAggregateCursor {
    public enum Comparison { GREATER_THAN, LESS_THAN }

    /** One disjunct in a lexicographic continuation predicate. */
    public record ContinuationBranch(List<Object> equalPrefix, int keyIndex,
                                     Object cursorValue, boolean includesNull,
                                     Comparison comparison) {
        public ContinuationBranch {
            equalPrefix = java.util.Collections.unmodifiableList(new ArrayList<>(equalPrefix));
            if (keyIndex < 0 || equalPrefix.size() != keyIndex)
                throw new IllegalArgumentException("Continuation prefix does not match its key index.");
            if (cursorValue == null && (includesNull || comparison != null))
                throw new IllegalArgumentException("A NULLS LAST cursor key has no later value at the same key.");
            if (cursorValue != null && comparison == null)
                throw new IllegalArgumentException("A non-null cursor key requires a comparison direction.");
        }
    }
    public record Slot(boolean nullValue, String value) {
        public Slot {
            if (nullValue && value != null)
                throw new IllegalArgumentException("A null aggregate cursor slot cannot carry a value.");
            if (!nullValue && value == null)
                throw new IllegalArgumentException("A non-null aggregate cursor slot requires a value.");
        }

        public static Slot of(Object value) {
            return value == null ? new Slot(true, null) : new Slot(false, OrmCursor.format(value));
        }
    }

    private OrmAggregateCursor() {}

    /** Encodes a sequence of aggregate values using canonical null-rank/value pairs. */
    public static String encode(List<?> values) {
        Objects.requireNonNull(values, "values");
        var components = new ArrayList<Object>(values.size() * 2);
        for (Object value : values) {
            components.add(value == null ? "1" : "0");
            components.add(value == null ? null : OrmCursor.format(value));
        }
        return OrmCursor.encode(components);
    }

    /** Decodes and validates the exact two-component representation for every logical key. */
    public static List<Slot> decode(String cursor, int expectedKeys) {
        if (expectedKeys < 1) throw new IllegalArgumentException("At least one cursor key is required.");
        List<String> components = OrmCursor.decode(cursor);
        if (components.size() != expectedKeys * 2)
            throw new IllegalStateException("The aggregate cursor does not match the ordering of this query.");
        var slots = new ArrayList<Slot>(expectedKeys);
        for (int i = 0; i < components.size(); i += 2) {
            String rank = components.get(i);
            String value = components.get(i + 1);
            if ("1".equals(rank)) {
                if (value != null) throw new IllegalStateException("A null-rank cursor slot must have a null value.");
                slots.add(new Slot(true, null));
            } else if ("0".equals(rank)) {
                if (value == null) throw new IllegalStateException("A non-null cursor slot must have a value.");
                slots.add(new Slot(false, value));
            } else {
                throw new IllegalStateException("The aggregate cursor contains an invalid null rank.");
            }
        }
        return List.copyOf(slots);
    }

    /** Decodes aggregate cursor slots and coerces each non-null value to its mapped Java type. */
    public static List<Object> decodeTyped(String cursor, List<Class<?>> valueTypes) {
        Objects.requireNonNull(valueTypes, "valueTypes");
        var slots = decode(cursor, valueTypes.size());
        var values = new ArrayList<Object>(slots.size());
        for (int i = 0; i < slots.size(); i++) {
            Slot slot = slots.get(i);
            values.add(slot.nullValue() ? null : OrmValues.coerce(slot.value(), valueTypes.get(i)));
        }
        return java.util.Collections.unmodifiableList(values);
    }

    /**
     * Builds the ordered disjunction for a strict keyset continuation. Each branch represents
     * equality on all preceding keys followed by the first differing key. NULLS LAST is
     * direction-independent: a non-null cursor admits NULL as a later value; a NULL cursor
     * admits no later value for that key.
     */
    public static List<ContinuationBranch> continuationBranches(
            List<?> cursorValues, List<Boolean> descending) {
        if (cursorValues.size() != descending.size())
            throw new IllegalArgumentException("Aggregate cursor tuple arity does not match its order.");
        var branches = new ArrayList<ContinuationBranch>();
        var prefix = new ArrayList<Object>();
        for (int i = 0; i < cursorValues.size(); i++) {
            Object value = cursorValues.get(i);
            if (value != null) {
                branches.add(new ContinuationBranch(prefix, i, value, true,
                        descending.get(i) ? Comparison.LESS_THAN : Comparison.GREATER_THAN));
            }
            prefix.add(value);
        }
        return List.copyOf(branches);
    }

    /**
     * Returns whether a candidate tuple sorts strictly after a cursor tuple under NULLS LAST.
     * Values must already be coerced to their mapped scalar types. This is shared by provider
     * conformance tests; SQL providers must compile the equivalent lexicographic predicate.
     */
    public static boolean isAfter(List<?> candidate, List<?> cursorValues, List<Boolean> descending) {
        if (candidate.size() != cursorValues.size() || candidate.size() != descending.size())
            throw new IllegalArgumentException("Aggregate cursor tuple arity does not match its order.");
        for (int i = 0; i < candidate.size(); i++) {
            Object left = candidate.get(i);
            Object right = cursorValues.get(i);
            if (left == right) continue;
            if (left == null) return right != null; // NULLS LAST
            if (right == null) return false;
            int comparison = OrmValues.compare(left, right);
            if (comparison == 0) continue;
            return descending.get(i) ? comparison < 0 : comparison > 0;
        }
        return false;
    }
}
