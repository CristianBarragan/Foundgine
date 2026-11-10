package com.foundgine.providers.storage.orm.jpa;

import com.foundgine.providers.storage.orm.OrmEntityMapping;
import com.foundgine.providers.storage.orm.OrmFieldBinding;
import com.foundgine.providers.storage.orm.OrmPredicate;

import jakarta.persistence.criteria.CriteriaBuilder;
import jakarta.persistence.criteria.Expression;
import jakarta.persistence.criteria.Path;
import jakarta.persistence.criteria.Predicate;
import jakarta.persistence.criteria.AbstractQuery;
import jakarta.persistence.criteria.From;
import jakarta.persistence.criteria.Subquery;
import jakarta.persistence.criteria.Root;

import java.util.ArrayList;
import java.util.List;

/** Translates {@link OrmPredicate} into Criteria predicates; every value is a bound parameter. */
final class JpaPredicates {
    private JpaPredicates() {}


    /** Builds a correlated aggregate expression for ORDER BY and a private tuple slot. */
    @SuppressWarnings({"unchecked", "rawtypes"})
    static Expression<?> aggregateOrderExpression(CriteriaBuilder cb, AbstractQuery<?> owner,
            From<?, ?> parent, OrmEntityMapping parentMap,
            com.foundgine.providers.storage.orm.OrmOrder order, JpaParameterBindings bindings) {
        var parentKey = parentMap.field(order.link().parentField());
        var childKey = order.target().field(order.link().childField());
        if (parentKey.type() != childKey.type())
            throw new IllegalArgumentException("Relationship key types must match exactly.");
        Class resultType = order.aggregate() == com.foundgine.providers.storage.orm.OrmOrder.AggregateKind.COUNT
                ? Long.class : order.target().field(order.field()).type();
        Subquery subquery = owner.subquery(resultType);
        Root<?> child = subquery.from(order.target().type());
        Expression aggregate;
        if (order.aggregate() == com.foundgine.providers.storage.orm.OrmOrder.AggregateKind.COUNT) {
            aggregate = cb.count(child);
        } else {
            var value = order.target().field(order.field());
            Expression path = child.get(value.name());
            aggregate = order.aggregate() == com.foundgine.providers.storage.orm.OrmOrder.AggregateKind.MIN
                    ? cb.least((Expression<Comparable>) path)
                    : cb.greatest((Expression<Comparable>) path);
        }
        Predicate correlation = cb.equal(child.get(childKey.name()), parent.get(parentKey.name()));
        Predicate auth = translate(cb, subquery, child, order.target(), order.authorization(), bindings);
        subquery.select(aggregate).where(cb.and(correlation, auth));
        return subquery;
    }

    @SuppressWarnings({"unchecked", "rawtypes"})
    static Predicate translate(
            CriteriaBuilder cb, AbstractQuery<?> owner, From<?, ?> root, OrmEntityMapping map, OrmPredicate p,
            JpaParameterBindings bindings) {
        if (p instanceof OrmPredicate.Const c) return c.value() ? cb.conjunction() : cb.disjunction();
        if (p instanceof OrmPredicate.And a) {
            return cb.and(a.terms().stream().map(t -> translate(cb, owner, root, map, t, bindings)).toArray(Predicate[]::new));
        }
        if (p instanceof OrmPredicate.Or o) {
            return cb.or(o.terms().stream().map(t -> translate(cb, owner, root, map, t, bindings)).toArray(Predicate[]::new));
        }
        if (p instanceof OrmPredicate.Not n) return cb.not(translate(cb, owner, root, map, n.term(), bindings));
        if (p instanceof OrmPredicate.Quantified quantified) {
            OrmFieldBinding parentKey = map.field(quantified.link().parentField());
            OrmFieldBinding childKey = quantified.target().field(quantified.link().childField());
            if (parentKey.type() != childKey.type()) {
                throw new IllegalArgumentException("Relationship key types must match exactly: "
                        + parentKey.type().getName() + " != " + childKey.type().getName() + ".");
            }
            Subquery<Integer> subquery = owner.subquery(Integer.class);
            Root<?> child = subquery.from(quantified.target().type());
            Predicate correlation = cb.equal(child.get(childKey.name()), root.get(parentKey.name()));
            Predicate childAuthorization = translate(cb, subquery, child, quantified.target(),
                    quantified.authorization(), bindings);
            Predicate childWhere = translate(cb, subquery, child, quantified.target(),
                    quantified.where(), bindings);
            Predicate match = switch (quantified.quantifier()) {
                case SOME -> cb.and(childAuthorization, childWhere);
                case NONE -> cb.and(childAuthorization, childWhere);
                case ALL -> cb.and(childAuthorization, cb.not(childWhere));
            };
            subquery.select(cb.literal(1)).where(cb.and(correlation, match));
            Predicate hasCounterexample = cb.exists(subquery);
            return quantified.quantifier() == OrmPredicate.Quantifier.SOME
                    ? hasCounterexample : cb.not(hasCounterexample);
        }
        if (p instanceof OrmPredicate.Aggregate aggregate) {
            OrmFieldBinding parentKey = map.field(aggregate.link().parentField());
            OrmFieldBinding childKey = aggregate.target().field(aggregate.link().childField());
            if (parentKey.type() != childKey.type())
                throw new IllegalArgumentException("Relationship key types must match exactly.");
            Subquery subquery;
            Root<?> child;
            Expression aggregateValue;
            if (aggregate.kind() == OrmPredicate.AggregateKind.COUNT) {
                subquery = owner.subquery(Long.class);
                child = subquery.from(aggregate.target().type());
                aggregateValue = cb.count(child);
            } else {
                OrmFieldBinding valueField = aggregate.target().field(aggregate.valueField());
                if (!valueField.isOrderable() || !Comparable.class.isAssignableFrom(valueField.type()))
                    throw new IllegalArgumentException("MIN/MAX require an orderable scalar field.");
                subquery = owner.subquery(valueField.type());
                child = subquery.from(aggregate.target().type());
                Expression valuePath = child.get(valueField.name());
                aggregateValue = aggregate.kind() == OrmPredicate.AggregateKind.MIN
                        ? cb.least((Expression<Comparable>) valuePath)
                        : cb.greatest((Expression<Comparable>) valuePath);
            }
            Predicate correlation = cb.equal(child.get(childKey.name()), root.get(parentKey.name()));
            Predicate auth = translate(cb, subquery, child, aggregate.target(), aggregate.authorization(), bindings);
            Predicate filter = translate(cb, subquery, child, aggregate.target(), aggregate.where(), bindings);
            subquery.select(aggregateValue).where(cb.and(correlation, auth, filter));
            if (aggregate.expected() == null) {
                if (aggregate.kind() == OrmPredicate.AggregateKind.COUNT)
                    return aggregate.comparison() == OrmPredicate.Op.EQ ? cb.disjunction() : cb.conjunction();
                return cb.disjunction();
            }
            Class valueType = aggregate.kind() == OrmPredicate.AggregateKind.COUNT
                    ? Long.class : aggregate.target().field(aggregate.valueField()).type();
            Expression parameter = bindings.add(cb, valueType, aggregate.expected());
            Predicate comparison = switch (aggregate.comparison()) {
                case EQ -> cb.equal(subquery, parameter);
                case NEQ -> cb.notEqual(subquery, parameter);
                case GT -> cb.greaterThan((Expression<Comparable>) subquery, (Expression<Comparable>) parameter);
                case GTE -> cb.greaterThanOrEqualTo((Expression<Comparable>) subquery, (Expression<Comparable>) parameter);
                case LT -> cb.lessThan((Expression<Comparable>) subquery, (Expression<Comparable>) parameter);
                case LTE -> cb.lessThanOrEqualTo((Expression<Comparable>) subquery, (Expression<Comparable>) parameter);
            };
            return aggregate.kind() == OrmPredicate.AggregateKind.COUNT ? comparison
                    : cb.and(cb.isNotNull(subquery), comparison);
        }
        if (p instanceof OrmPredicate.Exists exists) {
            OrmFieldBinding parentKey = map.field(exists.link().parentField());
            OrmFieldBinding childKey = exists.target().field(exists.link().childField());
            if (parentKey.type() != childKey.type()) {
                throw new IllegalArgumentException("Relationship key types must match exactly: "
                        + parentKey.type().getName() + " != " + childKey.type().getName() + ".");
            }
            Subquery<Integer> subquery = owner.subquery(Integer.class);
            Root<?> child = subquery.from(exists.target().type());
            Path<?> parentPath = root.get(parentKey.name());
            Path<?> childPath = child.get(childKey.name());
            Predicate correlation = cb.equal(childPath, parentPath);
            Predicate childAuthorization = translate(
                    cb, subquery, child, exists.target(), exists.authorization(), bindings);
            Predicate childWhere = translate(cb, subquery, child, exists.target(), exists.where(), bindings);
            subquery.select(cb.literal(1)).where(cb.and(correlation, childAuthorization, childWhere));
            return cb.exists(subquery);
        }
        if (p instanceof OrmPredicate.In in) {
            OrmFieldBinding field = map.field(in.field());
            List<Object> values = new ArrayList<>(in.values().size());
            for (Object value : in.values()) {
                // Foundgine defines IN as two-valued set membership: NULL list items are ignored
                // and a NULL field never matches. This matches InMemoryOrmQuerySource and keeps
                // NOT(IN(...)) as the exact boolean complement of the same membership test.
                if (value == null) continue;
                validateValue(field, value, "IN");
                values.add(value);
            }
            // Jakarta Persistence requires a non-empty literal IN list. Empty and NULL-only
            // lists are the semantic false predicate, independent of provider/dialect quirks.
            if (values.isEmpty()) return cb.disjunction();
            Path<Object> path = (Path<Object>) root.get(field.name());
            // Force IN into Foundgine's two-valued predicate domain. SQL IN on a NULL field
            // is UNKNOWN; adding IS NOT NULL makes the compound predicate FALSE so NOT can
            // safely produce TRUE, matching the in-memory semantic evaluator.
            var inPredicate = cb.in(path);
            for (Object value : values) {
                @SuppressWarnings("unchecked")
                Class<Object> valueType = (Class<Object>) field.type();
                inPredicate.value(bindings.add(cb, valueType, value));
            }
            return cb.and(cb.isNotNull(path), inPredicate);
        }

        var c = (OrmPredicate.Compare) p;
        OrmFieldBinding field = map.field(c.field());
        Object value = c.value();
        if (value != null) validateValue(field, value, c.op().name());
        if (c.op() == OrmPredicate.Op.GT || c.op() == OrmPredicate.Op.GTE
                || c.op() == OrmPredicate.Op.LT || c.op() == OrmPredicate.Op.LTE) {
            if (!field.isOrderable() || !Comparable.class.isAssignableFrom(field.type())) {
                throw new IllegalArgumentException(
                        "Ordering comparison " + c.op() + " is not supported for field '" + field.name()
                                + "' of type " + field.type().getName() + ".");
            }
        }
        Path path = root.get(field.name());
        if (value == null) return c.op() == OrmPredicate.Op.EQ ? cb.isNull(path) : cb.isNotNull(path);
        @SuppressWarnings("unchecked")
        Class<Object> valueType = (Class<Object>) field.type();
        Expression<Object> parameter = bindings.add(cb, valueType, value);
        // The parameter binder is intentionally type-erased because it serves every field
        // type. Ordering operators have already validated that this field is Comparable;
        // bridge through Expression<?> before applying the Criteria API's comparable type.
        @SuppressWarnings("unchecked")
        Expression<Comparable> comparableParameter = (Expression<Comparable>) (Expression<?>) parameter;
        @SuppressWarnings("unchecked")
        Expression<Comparable> comparablePath = (Expression<Comparable>) (Expression<?>) path;
        Predicate comparison = switch (c.op()) {
            case EQ -> cb.equal(path, parameter);
            case NEQ -> cb.notEqual(path, parameter);
            case GT -> cb.greaterThan(comparablePath, comparableParameter);
            case GTE -> cb.greaterThanOrEqualTo(comparablePath, comparableParameter);
            case LT -> cb.lessThan(comparablePath, comparableParameter);
            case LTE -> cb.lessThanOrEqualTo(comparablePath, comparableParameter);
        };
        // Foundgine predicates compose in a two-valued boolean algebra. SQL comparisons on NULL
        // produce UNKNOWN, so guard them to FALSE before NOT/AND/OR composition.
        return cb.and(cb.isNotNull(path), comparison);
    }

    private static void validateValue(OrmFieldBinding field, Object value, String operation) {
        if (!field.type().isInstance(value)) {
            throw new IllegalArgumentException(
                    operation + " value for field '" + field.name() + "' must be "
                            + field.type().getName() + ", not " + value.getClass().getName() + ".");
        }
    }
}
