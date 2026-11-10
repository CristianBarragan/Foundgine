package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.FieldId;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Objects;

/**
 * One single-entity read. {@code where} is a list of clauses that are AND-ed; authorization is
 * always the first clause and is never merged with later ones, so later clauses can only narrow.
 * A source reads only {@code select}. Continuation values, when present, align exactly with
 * {@code order} and remain private to the provider.
 */
public record OrmFetchRequest(
        OrmEntityMapping entity,
        List<FieldId> select,
        List<OrmPredicate> where,
        List<OrmOrder> order,
        Integer offset,
        Integer limit,
        List<Object> continuationValues) {
    /** Source-compatible constructor for ordinary and non-cursor fetches. */
    public OrmFetchRequest(OrmEntityMapping entity, List<FieldId> select,
            List<OrmPredicate> where, List<OrmOrder> order, Integer offset, Integer limit) {
        this(entity, select, where, order, offset, limit, List.of());
    }

    public OrmFetchRequest {
        Objects.requireNonNull(entity, "entity");
        select = List.copyOf(select);
        where = List.copyOf(where);
        order = List.copyOf(order);
        continuationValues = Collections.unmodifiableList(new ArrayList<>(continuationValues));
        if (!continuationValues.isEmpty() && continuationValues.size() != order.size()) {
            throw new IllegalArgumentException("Continuation values must align with every order key.");
        }
        if (!continuationValues.isEmpty() && order.isEmpty()) {
            throw new IllegalArgumentException("Continuation values require an order specification.");
        }
    }
}
