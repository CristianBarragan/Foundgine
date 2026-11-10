package com.foundgine.providers.storage.orm.jpa;

import com.foundgine.providers.storage.orm.IOrmMutationSource;
import com.foundgine.providers.storage.orm.OrmEntityMapping;
import com.foundgine.providers.storage.orm.OrmPredicate;
import com.foundgine.providers.storage.orm.OrmPredicateValidator;

import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityTransaction;
import jakarta.persistence.criteria.CriteriaBuilder;
import jakarta.persistence.criteria.CriteriaQuery;
import jakarta.persistence.criteria.Predicate;
import jakarta.persistence.criteria.Root;

import java.util.List;
import java.util.Objects;

/**
 * Resource-local JPA {@link IOrmMutationSource}. One {@code EntityManager} must be used by one
 * batch at a time. If the persistence context already has an active transaction the batch joins it
 * (and on failure only marks it rollback-only); otherwise the batch owns begin/commit/rollback.
 *
 * <p>Matching rows are loaded as managed entities and changed through setters, so lifecycle
 * callbacks and optimistic version checks run as usual. No pessimistic lock is taken; add one in a
 * subclass if concurrent writers to the same rows are expected.
 */
public final class JpaOrmMutationSource implements IOrmMutationSource {
    private final EntityManager em;

    public JpaOrmMutationSource(EntityManager em) {
        this.em = Objects.requireNonNull(em, "em");
    }

    @Override
    public Transaction begin() {
        EntityTransaction tx = em.getTransaction();
        boolean owns = !tx.isActive();
        if (owns) tx.begin();
        return new Transaction() {
            boolean done;

            @Override
            public void commit() {
                if (done) return;
                done = true;
                if (owns) tx.commit();
            }

            @Override
            public void rollback() {
                if (done) return;
                done = true;
                if (owns) {
                    if (tx.isActive()) tx.rollback();
                } else {
                    tx.setRollbackOnly();
                }
            }

            @Override
            public void close() {
                if (!done) rollback();
            }
        };
    }

    @Override
    public List<Object> find(OrmEntityMapping map, OrmPredicate where) {
        CriteriaBuilder cb = em.getCriteriaBuilder();
        @SuppressWarnings("unchecked")
        Class<Object> type = (Class<Object>) map.type();
        CriteriaQuery<Object> cq = cb.createQuery(type);
        Root<Object> root = cq.from(type);
        var bindings = new JpaParameterBindings();
        OrmPredicateValidator.validate(where);
        Predicate predicate = JpaPredicates.translate(cb, cq, root, map, where, bindings);
        cq.select(root).where(predicate);
        var query = em.createQuery(cq);
        bindings.bind(query);
        return query.getResultList();
    }

    @Override
    public void persist(OrmEntityMapping map, Object entity) {
        em.persist(entity);
    }

    @Override
    public void remove(OrmEntityMapping map, Object entity) {
        em.remove(entity);
    }

    @Override
    public void flush() {
        em.flush();
    }
}
