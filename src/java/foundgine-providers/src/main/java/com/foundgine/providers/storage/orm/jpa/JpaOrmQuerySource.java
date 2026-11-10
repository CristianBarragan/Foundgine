package com.foundgine.providers.storage.orm.jpa;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.providers.storage.orm.IOrmQuerySource;
import com.foundgine.providers.storage.orm.OrmEntityMapping;
import com.foundgine.providers.storage.orm.OrmFetchRequest;
import com.foundgine.providers.storage.orm.OrmFetchResult;
import com.foundgine.providers.storage.orm.OrmPredicate;
import com.foundgine.providers.storage.orm.OrmOrder;
import com.foundgine.providers.storage.orm.OrmPredicateValidator;

import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Tuple;
import jakarta.persistence.TypedQuery;
import jakarta.persistence.criteria.CriteriaBuilder;
import jakarta.persistence.criteria.Expression;
import jakarta.persistence.criteria.CriteriaQuery;
import jakarta.persistence.criteria.Order;
import jakarta.persistence.criteria.Path;
import jakarta.persistence.criteria.Predicate;
import jakarta.persistence.criteria.Root;
import jakarta.persistence.criteria.Selection;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.function.Supplier;

/**
 * Jakarta Persistence (JPA / Hibernate) {@link IOrmQuerySource}. Every fetch is a Criteria
 * <em>tuple</em> query selecting only the requested attributes: no entity is materialized, no
 * association is touched, and every value is bound as a query parameter by the persistence
 * provider.
 *
 * <p>This class is the only place that depends on {@code jakarta.persistence}. It is read-only:
 * it never begins a transaction; run it inside the caller's transaction if one is required.
 */
public final class JpaOrmQuerySource implements IOrmQuerySource {
    private final Supplier<EntityManager> entityManagers;
    private final boolean closeAfterUse;

    /** Uses the given (caller-managed) EntityManager; it is never closed here. */
    public JpaOrmQuerySource(EntityManager entityManager) {
        Objects.requireNonNull(entityManager, "entityManager");
        this.entityManagers = () -> entityManager;
        this.closeAfterUse = false;
    }

    /** Opens a short-lived EntityManager per fetch and closes it afterwards. */
    public JpaOrmQuerySource(EntityManagerFactory factory) {
        Objects.requireNonNull(factory, "factory");
        this.entityManagers = factory::createEntityManager;
        this.closeAfterUse = true;
    }

    @Override
    public List<Map<FieldId, Object>> fetch(OrmFetchRequest request) {
        EntityManager em = entityManagers.get();
        try {
            return run(em, request).rows();
        } finally {
            if (closeAfterUse && em.isOpen()) em.close();
        }
    }

    @Override
    public OrmFetchResult fetchWithPrivateOrderValues(OrmFetchRequest request) {
        EntityManager em = entityManagers.get();
        try {
            return run(em, request);
        } finally {
            if (closeAfterUse && em.isOpen()) em.close();
        }
    }

    private static OrmFetchResult run(EntityManager em, OrmFetchRequest r) {
        OrmEntityMapping map = r.entity();
        CriteriaBuilder cb = em.getCriteriaBuilder();
        CriteriaQuery<Tuple> cq = cb.createTupleQuery();
        Root<?> root = cq.from(map.type());

        var bindings = new JpaParameterBindings();
        var selections = new ArrayList<Selection<?>>(r.select().size());
        for (int i = 0; i < r.select().size(); i++) {
            Path<Object> path = root.get(map.field(r.select().get(i)).name());
            selections.add(path.alias("f" + i));
        }
        // Aggregate selectors are deliberately private tuple slots: they are available to the
        // SQL ordering operation but are never copied into the semantic result map.
        var privateOrderSlots = new ArrayList<Expression<?>>();
        var orderExpressions = new ArrayList<Expression<?>>(r.order().size());
        for (int i = 0; i < r.order().size(); i++) {
            var order = r.order().get(i);
            if (order.isAggregate()) {
                OrmPredicateValidator.validate(order.authorization());
                Expression<?> aggregate = JpaPredicates.aggregateOrderExpression(
                        cb, cq, root, map, order, bindings);
                privateOrderSlots.add(aggregate);
                orderExpressions.add(aggregate);
                selections.add(aggregate.alias("__order" + i));
            } else {
                orderExpressions.add(root.get(map.field(order.field()).name()));
            }
        }
        cq.multiselect(selections);

        // One Predicate per clause, authorization first: they are AND-ed by the persistence
        // provider and never merged, so later clauses can only narrow.
        var predicates = new ArrayList<Predicate>(r.where().size());
        for (OrmPredicate clause : r.where()) {
            OrmPredicateValidator.validate(clause);
            predicates.add(JpaPredicates.translate(cb, cq, root, map, clause, bindings));
        }
        if (!r.continuationValues().isEmpty()) {
            var descending = r.order().stream().map(OrmOrder::descending).toList();
            predicates.add(JpaAggregateContinuation.compile(
                    cb, orderExpressions, r.continuationValues(), descending, bindings));
        }
        if (!predicates.isEmpty()) cq.where(predicates.toArray(new Predicate[0]));

        if (!r.order().isEmpty()) {
            var orders = new ArrayList<Order>(r.order().size());
            int aggregateIndex = 0;
            for (var o : r.order()) {
                Expression<?> expression;
                if (o.isAggregate()) {
                    expression = privateOrderSlots.get(aggregateIndex++);
                    if (o.aggregate() != com.foundgine.providers.storage.orm.OrmOrder.AggregateKind.COUNT) {
                        // Stable NULLS LAST policy, independent of database defaults and direction.
                        orders.add(cb.asc(cb.<Integer>selectCase().when(cb.isNull(expression), 1).otherwise(0)));
                    }
                } else {
                    expression = root.get(map.field(o.field()).name());
                }
                orders.add(o.descending() ? cb.desc(expression) : cb.asc(expression));
            }
            cq.orderBy(orders);
        }

        TypedQuery<Tuple> query = em.createQuery(cq);
        if (r.offset() != null && r.offset() > 0) query.setFirstResult(r.offset());
        if (r.limit() != null && r.limit() > 0) query.setMaxResults(r.limit());
        bindings.bind(query);

        var result = new ArrayList<Map<FieldId, Object>>();
        var privateValues = new ArrayList<List<Object>>();
        var aggregateOrderIndexes = new ArrayList<Integer>();
        for (int i = 0; i < r.order().size(); i++) if (r.order().get(i).isAggregate()) aggregateOrderIndexes.add(i);
        for (Tuple tuple : query.getResultList()) {
            var row = new LinkedHashMap<FieldId, Object>(r.select().size());
            for (int i = 0; i < r.select().size(); i++) row.put(r.select().get(i), tuple.get(i));
            result.add(row);
            var privateRow = new ArrayList<Object>(aggregateOrderIndexes.size());
            for (int i = 0; i < aggregateOrderIndexes.size(); i++)
                privateRow.add(tuple.get(r.select().size() + i));
            privateValues.add(privateRow);
        }
        return new OrmFetchResult(result, privateValues);
    }
}
