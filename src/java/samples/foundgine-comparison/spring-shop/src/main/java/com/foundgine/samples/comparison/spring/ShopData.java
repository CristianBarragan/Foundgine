package com.foundgine.samples.comparison.spring;

import jakarta.persistence.EntityManager;
import jakarta.persistence.PersistenceContext;
import jakarta.persistence.criteria.Predicate;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.Map;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Transactional;

/** Read access through plain JPA Criteria. */
@Component
@Transactional(readOnly = true)
public class ShopData {
    @PersistenceContext private EntityManager em;

    /** Rows of {@code shape} matching {@code where}, restricted to {@code scopeAttr = scopeId} when a scope is given. */
    @SuppressWarnings("unchecked")
    <T> List<T> find(Filters.Shape shape, Map<String, Object> where, Map<String, Object> order, String scopeAttr, Integer scopeId) {
        var cb = em.getCriteriaBuilder();
        var q = cb.createQuery(Object.class);
        var root = q.from(shape.type());
        var ps = new ArrayList<Predicate>();
        if (scopeId != null) ps.add(cb.equal(root.get(scopeAttr), scopeId));
        if (where != null) ps.add(Filters.where(shape, where, root, q, cb));
        q.select(root).where(ps.toArray(Predicate[]::new));
        if (order != null) q.orderBy(Filters.orders(shape, order, root, cb));
        return (List<T>) em.createQuery(q).getResultList();
    }

    /** Rows of {@code type} whose {@code attribute} is one of {@code values}: the batch loader behind nested fields. */
    <T> List<T> findIn(Class<T> type, String attribute, Collection<Integer> values) {
        if (values.isEmpty()) return List.of();
        var cb = em.getCriteriaBuilder();
        var q = cb.createQuery(type);
        var root = q.from(type);
        return em.createQuery(q.select(root).where(root.get(attribute).in(values))).getResultList();
    }
}
