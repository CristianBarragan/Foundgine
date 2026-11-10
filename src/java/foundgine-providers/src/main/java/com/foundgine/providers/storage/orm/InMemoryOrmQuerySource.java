package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.function.Function;

/**
 * Reference {@link IOrmQuerySource} over plain rows. Evaluates the same {@link OrmPredicate}s a
 * database source translates, so provider logic (authorization, traversal, cursors) can be tested
 * without a database. Strings compare by {@link String#compareTo}; databases may use another
 * collation.
 */
public final class InMemoryOrmQuerySource implements IOrmQuerySource {
    private final Map<EntityId, List<Map<FieldId, Object>>> data = new LinkedHashMap<>();

    public InMemoryOrmQuerySource add(EntityId entity, Map<FieldId, Object> row) {
        data.computeIfAbsent(entity, k -> new ArrayList<>()).add(new LinkedHashMap<>(row));
        return this;
    }

    @Override
    public List<Map<FieldId, Object>> fetch(OrmFetchRequest r) {
        return fetchWithPrivateOrderValues(r).rows();
    }

    @Override
    public OrmFetchResult fetchWithPrivateOrderValues(OrmFetchRequest r) {
        var rows = new ArrayList<Map<FieldId, Object>>();
        for (var row : data.getOrDefault(r.entity().entityId(), List.of())) {
            boolean ok = true;
            for (var clause : r.where()) {
                OrmPredicateValidator.validate(clause);
                if (!evaluate(clause, row::get, related -> data.getOrDefault(related.entityId(), List.of()))) {
                    ok = false;
                    break;
                }
            }
            if (ok) rows.add(row);
        }
        if (!r.order().isEmpty()) {
            Comparator<Map<FieldId, Object>> cmp = null;
            for (var o : r.order()) {
                if (o.isAggregate()) OrmPredicateValidator.validate(o.authorization());
                Comparator<Map<FieldId, Object>> c = (a, b) -> {
                    Object av = orderValue(a, o);
                    Object bv = orderValue(b, o);
                    if (av == null || bv == null) {
                        if (av == null && bv == null) return 0;
                        return av == null ? 1 : -1; // NULLS LAST in both directions
                    }
                    int compared = OrmValues.compare(av, bv);
                    return o.descending() ? -compared : compared;
                };
                cmp = cmp == null ? c : cmp.thenComparing(c);
            }
            rows.sort(cmp);
        }
        int from = r.offset() == null ? 0 : Math.min(r.offset(), rows.size());
        int to = r.limit() == null ? rows.size() : Math.min(rows.size(), from + r.limit());
        var result = new ArrayList<Map<FieldId, Object>>();
        var privateValues = new ArrayList<List<Object>>();
        var aggregateOrders = r.order().stream().filter(OrmOrder::isAggregate).toList();
        for (var row : rows.subList(from, to)) {
            var projected = new LinkedHashMap<FieldId, Object>();
            for (FieldId f : r.select()) projected.put(f, row.get(f));
            result.add(projected);
            var privateRow = new ArrayList<Object>(aggregateOrders.size());
            for (var order : aggregateOrders) privateRow.add(orderValue(row, order));
            privateValues.add(privateRow);
        }
        return new OrmFetchResult(result, privateValues);
    }

    private Object orderValue(Map<FieldId, Object> parent, OrmOrder order) {
        if (!order.isAggregate()) return parent.get(order.field());
        Object parentKey = parent.get(order.link().parentField());
        if (parentKey == null) return order.aggregate() == OrmOrder.AggregateKind.COUNT ? 0L : null;
        var values = new ArrayList<Object>();
        for (var child : data.getOrDefault(order.target().entityId(), List.of())) {
            Object childKey = child.get(order.link().childField());
            if (childKey == null || !OrmValues.valueEquals(parentKey, childKey)
                    || !evaluate(order.authorization(), child::get,
                            target -> data.getOrDefault(target.entityId(), List.of()))) continue;
            if (order.aggregate() == OrmOrder.AggregateKind.COUNT) values.add(Boolean.TRUE);
            else {
                Object value = child.get(order.field());
                if (value != null) values.add(value);
            }
        }
        if (order.aggregate() == OrmOrder.AggregateKind.COUNT) return (long) values.size();
        if (values.isEmpty()) return null;
        return values.stream().reduce((left, right) -> {
            int c = OrmValues.compare(left, right);
            return order.aggregate() == OrmOrder.AggregateKind.MIN
                    ? (c <= 0 ? left : right) : (c >= 0 ? left : right);
        }).orElse(null);
    }

    /** Evaluates Foundgine's two-valued predicate contract, including explicit NULL tests. */
    static boolean evaluate(OrmPredicate p, Function<FieldId, Object> row) {
        return evaluate(p, row, ignored -> List.of());
    }

    static boolean evaluate(OrmPredicate p, Function<FieldId, Object> row,
            Function<com.foundgine.providers.storage.orm.OrmEntityMapping, List<Map<FieldId, Object>>> relatedRows) {
        if (p instanceof OrmPredicate.Const c) return c.value();
        if (p instanceof OrmPredicate.And a) return a.terms().stream().allMatch(t -> evaluate(t, row, relatedRows));
        if (p instanceof OrmPredicate.Or o) return o.terms().stream().anyMatch(t -> evaluate(t, row, relatedRows));
        if (p instanceof OrmPredicate.Not n) return !evaluate(n.term(), row, relatedRows);
        if (p instanceof OrmPredicate.Quantified quantified) {
            Object parentValue = row.apply(quantified.link().parentField());
            if (parentValue == null) return quantified.quantifier() != OrmPredicate.Quantifier.SOME;
            boolean foundMatch = false;
            boolean foundCounterexample = false;
            for (var child : relatedRows.apply(quantified.target())) {
                Object childValue = child.get(quantified.link().childField());
                if (childValue == null || !OrmValues.valueEquals(parentValue, childValue)
                        || !evaluate(quantified.authorization(), child::get, relatedRows)) continue;
                boolean matches = evaluate(quantified.where(), child::get, relatedRows);
                foundMatch |= matches;
                foundCounterexample |= !matches;
            }
            return switch (quantified.quantifier()) {
                case SOME -> foundMatch;
                case NONE -> !foundMatch;
                case ALL -> !foundCounterexample;
            };
        }
        if (p instanceof OrmPredicate.Aggregate aggregate) {
            Object parentValue = row.apply(aggregate.link().parentField());
            if (parentValue == null) return false;
            var matching = new ArrayList<Object>();
            for (var child : relatedRows.apply(aggregate.target())) {
                Object childKey = child.get(aggregate.link().childField());
                if (childKey == null || !OrmValues.valueEquals(parentValue, childKey)
                        || !evaluate(aggregate.authorization(), child::get, relatedRows)
                        || !evaluate(aggregate.where(), child::get, relatedRows)) continue;
                if (aggregate.kind() == OrmPredicate.AggregateKind.COUNT) matching.add(Boolean.TRUE);
                else {
                    Object value = child.get(aggregate.valueField());
                    if (value != null) matching.add(value);
                }
            }
            Object actual;
            if (aggregate.kind() == OrmPredicate.AggregateKind.COUNT) actual = (long) matching.size();
            else if (matching.isEmpty()) actual = null;
            else actual = matching.stream().reduce((a, b) -> {
                int cmp = OrmValues.compare(a, b);
                return aggregate.kind() == OrmPredicate.AggregateKind.MIN
                        ? (cmp <= 0 ? a : b) : (cmp >= 0 ? a : b);
            }).orElse(null);
            if (actual == null || aggregate.expected() == null) return false;
            int cmp = OrmValues.compare(actual, aggregate.expected());
            return switch (aggregate.comparison()) {
                case EQ -> cmp == 0; case NEQ -> cmp != 0; case GT -> cmp > 0;
                case GTE -> cmp >= 0; case LT -> cmp < 0; case LTE -> cmp <= 0;
            };
        }
        if (p instanceof OrmPredicate.Exists exists) {
            Object parentValue = row.apply(exists.link().parentField());
            if (parentValue == null) return false;
            return relatedRows.apply(exists.target()).stream().anyMatch(child -> {
                Object childValue = child.get(exists.link().childField());
                return childValue != null && OrmValues.valueEquals(parentValue, childValue)
                        && evaluate(exists.authorization(), child::get, relatedRows)
                        && evaluate(exists.where(), child::get, relatedRows);
            });
        }
        if (p instanceof OrmPredicate.In in) {
            Object actual = row.apply(in.field());
            return actual != null && in.values().stream().anyMatch(v -> OrmValues.valueEquals(actual, v));
        }
        var c = (OrmPredicate.Compare) p;
        Object actual = row.apply(c.field());
        // SQL semantics: any comparison with NULL is false, except explicit IS [NOT] NULL.
        if (c.value() == null) return c.op() == OrmPredicate.Op.EQ ? actual == null : actual != null;
        if (actual == null) return false;
        int cmp = OrmValues.compare(actual, c.value());
        return switch (c.op()) {
            case EQ -> cmp == 0;
            case NEQ -> cmp != 0;
            case GT -> cmp > 0;
            case GTE -> cmp >= 0;
            case LT -> cmp < 0;
            case LTE -> cmp <= 0;
        };
    }
}
