package com.foundgine.providers.storage.orm.jpa;

import jakarta.persistence.Query;
import jakarta.persistence.criteria.CriteriaBuilder;
import jakarta.persistence.criteria.ParameterExpression;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/** Explicit, typed Criteria parameters owned by one query execution. */
final class JpaParameterBindings {
    private record Binding(ParameterExpression<?> parameter, Object value) {}

    private final List<Binding> bindings = new ArrayList<>();

    <T> ParameterExpression<T> add(CriteriaBuilder cb, Class<T> type, T value) {
        Objects.requireNonNull(cb, "cb");
        Objects.requireNonNull(type, "type");
        Objects.requireNonNull(value, "value");
        ParameterExpression<T> parameter = cb.parameter(type, "fg" + bindings.size());
        bindings.add(new Binding(parameter, value));
        return parameter;
    }

    @SuppressWarnings({"rawtypes", "unchecked"})
    void bind(Query query) {
        Objects.requireNonNull(query, "query");
        for (Binding binding : bindings) {
            query.setParameter((ParameterExpression) binding.parameter(), binding.value());
        }
    }

    int size() { return bindings.size(); }
}
