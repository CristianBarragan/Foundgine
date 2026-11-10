package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.FieldId;

import java.util.List;
import java.util.Objects;

/**
 * Provider-neutral, ORM-agnostic boolean expression over the fields of ONE entity. Values are
 * always carried as data (never concatenated into text), so every {@link IOrmQuerySource} binds
 * them as parameters.
 */
public sealed interface OrmPredicate {

    enum Op {
        EQ,
        NEQ,
        GT,
        GTE,
        LT,
        LTE
    }

    /** Constant true/false. */
    record Const(boolean value) implements OrmPredicate {}

    /**
     * {@code field op value}. {@code EQ null} means IS NULL and {@code NEQ null} IS NOT NULL; the
     * ordering operators never accept null.
     */
    record Compare(FieldId field, Op op, Object value) implements OrmPredicate {
        public Compare {
            Objects.requireNonNull(field, "field");
            Objects.requireNonNull(op, "op");
            if (value == null && op != Op.EQ && op != Op.NEQ) {
                throw new IllegalArgumentException("Ordering comparisons require a value.");
            }
        }
    }

    /**
     * Two-valued membership. NULL list members are ignored, a NULL field never matches, and an
     * empty (or NULL-only) list is false. This keeps NOT(IN(...)) a boolean complement across
     * the in-memory and database providers.
     */
    record In(FieldId field, List<Object> values) implements OrmPredicate {
        public In {
            Objects.requireNonNull(field, "field");
            values = java.util.Collections.unmodifiableList(new java.util.ArrayList<>(values));
        }
    }

    /** Relationship quantifiers over the authorization-visible child set. */
    enum Quantifier { SOME, NONE, ALL }

    /**
     * Quantifies over related rows visible under {@code authorization}. ALL uses vacuous truth:
     * when no authorized child exists, the predicate is true. Unauthorized children are outside
     * the quantified set and cannot make SOME/NONE/ALL succeed or fail.
     */
    record Quantified(Quantifier quantifier, OrmEntityMapping target, OrmLink link,
                      OrmPredicate authorization, OrmPredicate where) implements OrmPredicate {
        public Quantified {
            Objects.requireNonNull(quantifier, "quantifier");
            Objects.requireNonNull(target, "target");
            Objects.requireNonNull(link, "link");
            Objects.requireNonNull(authorization, "authorization");
            Objects.requireNonNull(where, "where");
            target.field(link.childField());
        }
    }

    /** Supported scalar aggregates over the authorization-visible related set. */
    enum AggregateKind { COUNT, MIN, MAX }

    /**
     * Compares an authorized relationship aggregate with a typed constant. COUNT is always
     * defined (zero for an empty set); MIN/MAX ignore null values and are null for an empty
     * non-null value set. A comparison against a null MIN/MAX result is always false.
     */
    record Aggregate(AggregateKind kind, OrmEntityMapping target, OrmLink link,
                     OrmPredicate authorization, OrmPredicate where,
                     com.foundgine.core.abstractions.FieldId valueField,
                     Op comparison, Object expected) implements OrmPredicate {
        public Aggregate {
            Objects.requireNonNull(kind, "kind");
            Objects.requireNonNull(target, "target");
            Objects.requireNonNull(link, "link");
            Objects.requireNonNull(authorization, "authorization");
            Objects.requireNonNull(where, "where");
            Objects.requireNonNull(comparison, "comparison");
            if (kind == AggregateKind.COUNT) {
                if (valueField != null) throw new IllegalArgumentException("COUNT does not accept a value field.");
                if (!(expected instanceof Long)) throw new IllegalArgumentException("COUNT comparison value must be Long.");
            } else {
                Objects.requireNonNull(valueField, "valueField");
                OrmFieldBinding value = target.field(valueField);
                if (!value.isOrderable() || !Comparable.class.isAssignableFrom(value.type()))
                    throw new IllegalArgumentException("MIN/MAX require an orderable scalar field.");
                if (expected != null && !value.type().isInstance(expected))
                    throw new IllegalArgumentException("Aggregate comparison value must be " + value.type().getName() + ".");
            }
            if (expected == null && comparison != Op.EQ && comparison != Op.NEQ)
                throw new IllegalArgumentException("Ordering aggregate comparisons require a value.");
            target.field(link.childField());
        }
    }

    /**
     * Correlated existential test over a related entity. The relationship is expressed as
     * scalar key bindings so the semantic layer does not depend on JPA association metadata.
     * The child predicate must include that entity's authorization constraints.
     */
    record Exists(OrmEntityMapping target, OrmLink link, OrmPredicate authorization, OrmPredicate where) implements OrmPredicate {
        public Exists {
            Objects.requireNonNull(target, "target");
            Objects.requireNonNull(link, "link");
            Objects.requireNonNull(authorization, "authorization");
            Objects.requireNonNull(where, "where");
            target.field(link.childField());
        }
    }

    record And(List<OrmPredicate> terms) implements OrmPredicate {
        public And {
            terms = List.copyOf(terms);
        }
    }

    record Or(List<OrmPredicate> terms) implements OrmPredicate {
        public Or {
            terms = List.copyOf(terms);
        }
    }

    record Not(OrmPredicate term) implements OrmPredicate {
        public Not {
            Objects.requireNonNull(term, "term");
        }
    }

    static OrmPredicate and(OrmPredicate left, OrmPredicate right) {
        if (left instanceof Const c) return c.value() ? right : left;
        if (right instanceof Const c) return c.value() ? left : right;
        return new And(List.of(left, right));
    }

    static OrmPredicate or(OrmPredicate left, OrmPredicate right) {
        if (left instanceof Const c) return c.value() ? left : right;
        if (right instanceof Const c) return c.value() ? right : left;
        return new Or(List.of(left, right));
    }

    static OrmPredicate not(OrmPredicate term) {
        if (term instanceof Const c) return new Const(!c.value());
        return new Not(term);
    }
}
