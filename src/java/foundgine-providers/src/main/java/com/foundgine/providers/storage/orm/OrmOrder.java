package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.FieldId;
import java.util.Objects;

/** One ordinary or authorization-scoped relationship aggregate ordering key. */
public record OrmOrder(FieldId field, boolean descending, AggregateKind aggregate,
        OrmEntityMapping target, OrmLink link, OrmPredicate authorization) {
    public enum AggregateKind { NONE, COUNT, MIN, MAX }

    public OrmOrder(FieldId field, boolean descending) {
        this(field, descending, AggregateKind.NONE, null, null, null);
    }

    public OrmOrder {
        Objects.requireNonNull(field, "field");
        aggregate = aggregate == null ? AggregateKind.NONE : aggregate;
        if (aggregate == AggregateKind.NONE) {
            if (target != null || link != null || authorization != null)
                throw new IllegalArgumentException("Ordinary ordering cannot carry aggregate metadata.");
        } else {
            Objects.requireNonNull(target, "target");
            Objects.requireNonNull(link, "link");
            Objects.requireNonNull(authorization, "authorization");
            target.field(link.childField());
            if (aggregate != AggregateKind.COUNT) {
                var value = target.field(field);
                if (!value.isOrderable() || !Comparable.class.isAssignableFrom(value.type()))
                    throw new IllegalArgumentException("MIN/MAX ordering requires an orderable scalar field.");
            }
        }
    }

    public boolean isAggregate() { return aggregate != AggregateKind.NONE; }
}
