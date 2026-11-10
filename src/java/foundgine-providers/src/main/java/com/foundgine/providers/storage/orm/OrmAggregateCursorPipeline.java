package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.FieldId;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/**
 * Joins the private aggregate tuple channel to the plan-bound cursor envelope.
 * Aggregate values are followed by declared primary-key values, forming one total-order tuple.
 * This class does not authorize a request or make an otherwise unsupported page executable.
 */
public final class OrmAggregateCursorPipeline {
    public record Decoded(List<Object> aggregateValues, List<Object> primaryKeyValues,
            List<Object> orderedValues) {
        public Decoded {
            aggregateValues = java.util.Collections.unmodifiableList(new ArrayList<>(aggregateValues));
            primaryKeyValues = List.copyOf(primaryKeyValues);
            orderedValues = java.util.Collections.unmodifiableList(new ArrayList<>(orderedValues));
        }
    }

    private OrmAggregateCursorPipeline() {}


    /**
     * Encodes cursor values in the exact provider ORDER BY sequence. Aggregate values come
     * from the private tuple channel; ordinary values (including primary-key tie-breakers)
     * come only from the semantic row. This supports interleaved aggregate and ordinary keys.
     */
    public static String encodeOrdered(String canonicalPlanIdentity, OrmEntityMapping root,
            List<OrmOrder> order, List<?> privateAggregateValues,
            java.util.Map<FieldId, Object> semanticRow) {
        Objects.requireNonNull(root, "root");
        Objects.requireNonNull(order, "order");
        Objects.requireNonNull(privateAggregateValues, "privateAggregateValues");
        Objects.requireNonNull(semanticRow, "semanticRow");
        long expectedAggregates = order.stream().filter(OrmOrder::isAggregate).count();
        if (expectedAggregates == 0 || expectedAggregates != privateAggregateValues.size())
            throw new IllegalArgumentException("Private aggregate tuple does not match aggregate order slots.");
        var tuple = new ArrayList<Object>(order.size());
        int aggregateIndex = 0;
        for (OrmOrder term : order) {
            if (term.isAggregate()) {
                tuple.add(privateAggregateValues.get(aggregateIndex++));
            } else {
                if (!semanticRow.containsKey(term.field()))
                    throw new IllegalArgumentException("Semantic row is missing cursor order field " + term.field() + ".");
                Object value = semanticRow.get(term.field());
                if (root.field(term.field()).primaryKey() && value == null)
                    throw new IllegalArgumentException("Primary-key cursor values cannot be NULL.");
                tuple.add(value);
            }
        }
        return OrmAggregateCursorBinding.encode(
                OrmAggregateCursorBinding.fingerprint(canonicalPlanIdentity), tuple);
    }

    /** Decodes a cursor using the mapped type of each key in the exact ORDER BY sequence. */
    public static Decoded decodeOrdered(String cursor, String canonicalPlanIdentity,
            OrmEntityMapping root, List<OrmOrder> order) {
        Objects.requireNonNull(root, "root");
        Objects.requireNonNull(order, "order");
        if (order.stream().noneMatch(OrmOrder::isAggregate))
            throw new IllegalArgumentException("Ordered aggregate cursor requires an aggregate key.");
        var types = new ArrayList<Class<?>>(order.size());
        int aggregateCount = 0;
        for (OrmOrder term : order) {
            if (term.isAggregate()) {
                aggregateCount++;
                types.add(term.aggregate() == OrmOrder.AggregateKind.COUNT
                        ? Long.class : term.target().field(term.field()).type());
            } else {
                types.add(root.field(term.field()).type());
            }
        }
        var values = OrmAggregateCursorBinding.decode(cursor,
                OrmAggregateCursorBinding.fingerprint(canonicalPlanIdentity), types);
        for (int i = 0; i < order.size(); i++) {
            if (!order.get(i).isAggregate() && root.field(order.get(i).field()).primaryKey()
                    && values.get(i) == null)
                throw new IllegalArgumentException("Primary-key cursor values cannot be NULL.");
        }
        var aggregates = new ArrayList<Object>(aggregateCount);
        for (int i = 0; i < order.size(); i++) if (order.get(i).isAggregate()) aggregates.add(values.get(i));
        return new Decoded(aggregates, List.of(), values);
    }

    /**
     * Decodes a public aggregate cursor and creates the provider fetch request carrying its
     * private continuation tuple. The supplied identity must be produced by the trusted
     * execution planner and include entity, filters, effective authorization, ordering and
     * cursor schema version. This helper does not grant authorization; the caller must rebuild
     * the current authorization predicates in {@code where} for every page.
     */
    public static OrmFetchRequest continuationRequest(String cursor, String canonicalPlanIdentity,
            OrmEntityMapping root, List<FieldId> select, List<OrmPredicate> where,
            List<OrmOrder> order, Integer limit) {
        Objects.requireNonNull(cursor, "cursor");
        Objects.requireNonNull(canonicalPlanIdentity, "canonicalPlanIdentity");
        Decoded decoded = decodeOrdered(cursor, canonicalPlanIdentity, root, order);
        return new OrmFetchRequest(root, select, where, order, null, limit,
                decoded.orderedValues());
    }

    /** Emits the next-page token from one row and its separately transported aggregate slots. */
    public static String nextPageCursor(String canonicalPlanIdentity, OrmEntityMapping root,
            List<OrmOrder> order, List<?> privateAggregateValues,
            java.util.Map<FieldId, Object> semanticRow) {
        return encodeOrdered(canonicalPlanIdentity, root, order, privateAggregateValues, semanticRow);
    }

    /** Derives sort directions from the same ordered terms used to encode the cursor. */
    public static List<OrmAggregateCursor.ContinuationBranch> continuationOrdered(
            Decoded cursor, List<OrmOrder> order) {
        Objects.requireNonNull(cursor, "cursor");
        Objects.requireNonNull(order, "order");
        if (cursor.orderedValues().size() != order.size())
            throw new IllegalArgumentException("Cursor tuple does not match ordered terms.");
        var directions = order.stream().map(OrmOrder::descending).toList();
        return OrmAggregateCursor.continuationBranches(cursor.orderedValues(), directions);
    }

    /** Emits a cursor from one fetched row without adding private values to its semantic map. */
    public static String encode(String canonicalPlanIdentity, List<?> privateAggregateValues,
            List<FieldId> primaryKeyFields, java.util.Map<FieldId, Object> semanticRow) {
        Objects.requireNonNull(primaryKeyFields, "primaryKeyFields");
        Objects.requireNonNull(semanticRow, "semanticRow");
        if (privateAggregateValues.isEmpty() || primaryKeyFields.isEmpty())
            throw new IllegalArgumentException("Aggregate cursors require aggregate slots and primary-key tie-breakers.");
        var tuple = new ArrayList<Object>(privateAggregateValues.size() + primaryKeyFields.size());
        tuple.addAll(privateAggregateValues);
        for (FieldId field : primaryKeyFields) {
            if (!semanticRow.containsKey(field) || semanticRow.get(field) == null)
                throw new IllegalArgumentException("A non-null primary-key cursor value is required for " + field + ".");
            tuple.add(semanticRow.get(field));
        }
        return OrmAggregateCursorBinding.encode(
                OrmAggregateCursorBinding.fingerprint(canonicalPlanIdentity), tuple);
    }

    /** Decodes all private aggregate slots and primary-key tie-breakers using mapped Java types. */
    public static Decoded decode(String cursor, String canonicalPlanIdentity,
            List<Class<?>> aggregateTypes, List<Class<?>> primaryKeyTypes) {
        Objects.requireNonNull(aggregateTypes, "aggregateTypes");
        Objects.requireNonNull(primaryKeyTypes, "primaryKeyTypes");
        if (aggregateTypes.isEmpty() || primaryKeyTypes.isEmpty())
            throw new IllegalArgumentException("Aggregate cursors require aggregate slots and primary-key tie-breakers.");
        var types = new ArrayList<Class<?>>(aggregateTypes.size() + primaryKeyTypes.size());
        types.addAll(aggregateTypes);
        types.addAll(primaryKeyTypes);
        var values = OrmAggregateCursorBinding.decode(cursor,
                OrmAggregateCursorBinding.fingerprint(canonicalPlanIdentity), types);
        for (int i = aggregateTypes.size(); i < values.size(); i++) {
            if (values.get(i) == null)
                throw new IllegalArgumentException("Primary-key cursor values cannot be NULL.");
        }
        return new Decoded(values.subList(0, aggregateTypes.size()),
                values.subList(aggregateTypes.size(), values.size()), values);
    }

    /**
     * Produces the same continuation descriptor sequence consumed by the JPA Criteria compiler.
     * Aggregate directions are supplied in aggregate sort order; primary keys are ascending.
     */
    public static List<OrmAggregateCursor.ContinuationBranch> continuation(
            Decoded cursor, List<Boolean> aggregateDescending) {
        Objects.requireNonNull(cursor, "cursor");
        Objects.requireNonNull(aggregateDescending, "aggregateDescending");
        if (aggregateDescending.size() != cursor.aggregateValues().size())
            throw new IllegalArgumentException("Aggregate direction count does not match private cursor slots.");
        var directions = new ArrayList<Boolean>(cursor.orderedValues().size());
        directions.addAll(aggregateDescending);
        for (int i = 0; i < cursor.primaryKeyValues().size(); i++) directions.add(false);
        return OrmAggregateCursor.continuationBranches(cursor.orderedValues(), directions);
    }
}
