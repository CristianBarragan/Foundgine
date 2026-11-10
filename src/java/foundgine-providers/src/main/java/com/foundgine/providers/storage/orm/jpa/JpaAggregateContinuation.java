package com.foundgine.providers.storage.orm.jpa;

import jakarta.persistence.criteria.CriteriaBuilder;
import jakarta.persistence.criteria.Expression;
import jakarta.persistence.criteria.Predicate;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/** Compiles canonical NULLS LAST aggregate/keyset tuples into typed Criteria predicates. */
final class JpaAggregateContinuation {
    private JpaAggregateContinuation() {}

    /**
     * Builds a strict lexicographic continuation predicate over the exact expressions used by
     * ORDER BY. Expressions may be private aggregate subqueries. Values must already have been
     * decoded/coerced using the query's mapped scalar types.
     */
    @SuppressWarnings({"unchecked", "rawtypes"})
    static Predicate compile(CriteriaBuilder cb, List<? extends Expression<?>> keys,
            List<?> cursorValues, List<Boolean> descending, JpaParameterBindings bindings) {
        Objects.requireNonNull(cb, "cb");
        Objects.requireNonNull(keys, "keys");
        Objects.requireNonNull(cursorValues, "cursorValues");
        Objects.requireNonNull(descending, "descending");
        Objects.requireNonNull(bindings, "bindings");
        if (keys.isEmpty() || keys.size() != cursorValues.size()
                || keys.size() != descending.size()) {
            throw new IllegalArgumentException("Continuation tuple does not match the query ordering.");
        }

        var disjunction = new ArrayList<Predicate>();
        var equalPrefix = new ArrayList<Predicate>();
        for (int i = 0; i < keys.size(); i++) {
            Expression key = keys.get(i);
            Object cursor = cursorValues.get(i);
            var branch = new ArrayList<Predicate>(equalPrefix);
            if (cursor != null) {
                Class<?> type = key.getJavaType();
                if (type == null || !type.isInstance(cursor)) {
                    throw new IllegalArgumentException("Cursor value type does not match ordering key " + i + ".");
                }
                var parameter = bindings.add(cb, (Class) type, cursor);
                Predicate comparison = descending.get(i)
                        ? cb.lessThan((Expression<Comparable>) key, (Expression<Comparable>) parameter)
                        : cb.greaterThan((Expression<Comparable>) key, (Expression<Comparable>) parameter);
                // NULLS LAST is independent of direction: every NULL follows every non-NULL.
                branch.add(cb.or(cb.and(cb.isNotNull(key), comparison), cb.isNull(key)));
                disjunction.add(cb.and(branch.toArray(Predicate[]::new)));
                equalPrefix.add(cb.equal(key, parameter));
            } else {
                // There is no value after NULL at this key. Continue only when a later key differs.
                equalPrefix.add(cb.isNull(key));
            }
        }
        return disjunction.isEmpty() ? cb.disjunction()
                : cb.or(disjunction.toArray(Predicate[]::new));
    }
}
