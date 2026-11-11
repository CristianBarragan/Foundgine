package com.foundgine.providers.storage.orm.jpa;

import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityTransaction;
import jakarta.persistence.TypedQuery;
import jakarta.persistence.criteria.CriteriaBuilder;
import jakarta.persistence.criteria.CriteriaQuery;
import jakarta.persistence.criteria.Predicate;
import jakarta.persistence.criteria.Root;

import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.function.Function;

/**
 * Typed, mechanical CRUD repository for one JPA entity type. Like the EF repository this class
 * deliberately contains no semantic authorization: semantic reads must use Foundgine's authorized
 * execution pipeline, and writes must be guarded by the application/capability layer.
 *
 * <p>The caller owns the EntityManager. Each write joins an active resource-local transaction or
 * creates and commits its own transaction. Entities loaded or merged by this repository are
 * detached after the operation so the repository does not leave accidental managed state behind.
 * A repository instance is not thread-safe.
 */
public class JpaOrmRepository<T> {
    private final EntityManager entityManager;
    private final Class<T> entityType;
    private final JpaEntityModel<T> generatedModel;

    public JpaOrmRepository(EntityManager entityManager, Class<T> entityType) {
        this.entityManager = Objects.requireNonNull(entityManager, "entityManager");
        this.entityType = Objects.requireNonNull(entityType, "entityType");
        this.generatedModel = null;
    }

    /** AOT-friendly constructor using generated direct entity accessors. */
    public JpaOrmRepository(EntityManager entityManager, JpaEntityModel<T> generatedModel) {
        this.entityManager = Objects.requireNonNull(entityManager, "entityManager");
        this.generatedModel = Objects.requireNonNull(generatedModel, "generatedModel");
        this.entityType = generatedModel.type();
    }

    /** The caller-managed persistence context, for transactions and provider-specific operations. */
    public EntityManager entityManager() { return entityManager; }

    /** Starts a Criteria query. JPA has no provider-neutral no-tracking equivalent to EF's AsNoTracking. */
    public TypedQuery<T> query() {
        CriteriaBuilder cb = entityManager.getCriteriaBuilder();
        CriteriaQuery<T> criteria = cb.createQuery(entityType);
        Root<T> root = criteria.from(entityType);
        criteria.select(root);
        return entityManager.createQuery(criteria);
    }

    public T get(Object id) {
        Objects.requireNonNull(id, "id");
        T entity = entityManager.find(entityType, id);
        if (entity != null && entityManager.contains(entity)) entityManager.detach(entity);
        return entity;
    }

    public List<T> list(Function<Root<T>, Predicate> predicate) {
        CriteriaBuilder cb = entityManager.getCriteriaBuilder();
        CriteriaQuery<T> criteria = cb.createQuery(entityType);
        Root<T> root = criteria.from(entityType);
        criteria.select(root);
        if (predicate != null) criteria.where(predicate.apply(root));
        List<T> rows = entityManager.createQuery(criteria).getResultList();
        // JPA has no AsNoTracking switch; detach the materialized rows to match repository list semantics.
        for (T row : rows) if (entityManager.contains(row)) entityManager.detach(row);
        return rows;
    }

    public List<T> list() { return list(null); }

    public boolean exists(Function<Root<T>, Predicate> predicate) {
        Objects.requireNonNull(predicate, "predicate");
        CriteriaBuilder cb = entityManager.getCriteriaBuilder();
        CriteriaQuery<Integer> criteria = cb.createQuery(Integer.class);
        Root<T> root = criteria.from(entityType);
        criteria.select(cb.literal(1)).where(predicate.apply(root));
        TypedQuery<Integer> query = entityManager.createQuery(criteria);
        query.setMaxResults(1);
        return !query.getResultList().isEmpty();
    }

    public long count(Function<Root<T>, Predicate> predicate) {
        CriteriaBuilder cb = entityManager.getCriteriaBuilder();
        CriteriaQuery<Long> criteria = cb.createQuery(Long.class);
        Root<T> root = criteria.from(entityType);
        criteria.select(cb.count(root));
        if (predicate != null) criteria.where(predicate.apply(root));
        return entityManager.createQuery(criteria).getSingleResult();
    }

    public long count() { return count(null); }

    public T add(T entity) {
        Objects.requireNonNull(entity, "entity");
        return inTransaction(() -> {
            entityManager.persist(entity);
            entityManager.flush();
            return entity;
        }, entity);
    }

    /** Merges the supplied entity; returns the detached managed copy containing generated values. */
    public T update(T entity) {
        Objects.requireNonNull(entity, "entity");
        T merged = inTransaction(() -> {
            T value = entityManager.merge(entity);
            entityManager.flush();
            return value;
        }, null);
        if (entityManager.contains(merged)) entityManager.detach(merged);
        return merged;
    }

    /** Sets one attribute of the row with this key (C# {@code UpdateAsync(id, x => x.Status, value)}). */
    public int update(Object id, String attribute, Object value) {
        Objects.requireNonNull(attribute, "attribute");
        // singletonMap, unlike Map.of, accepts a null value: it sets the column to null.
        return update(id, java.util.Collections.singletonMap(attribute, value));
    }

    /** Updates named persistent attributes by primary key. The key itself cannot be changed. */
    public int update(Object id, Map<String, ?> values) {
        Objects.requireNonNull(id, "id");
        Objects.requireNonNull(values, "values");
        if (values.isEmpty()) return 0;
        return inTransaction(() -> {
            T entity = entityManager.find(entityType, id);
            if (entity == null) return 0;
            try {
            for (var entry : values.entrySet()) {
                if (entry.getKey() == null || entry.getKey().isBlank())
                    throw new IllegalArgumentException("Attribute names must not be blank.");
                jakarta.persistence.metamodel.Attribute<? super T, ?> attribute;
                try {
                    attribute = entityManager.getMetamodel().entity(entityType).getAttribute(entry.getKey());
                } catch (IllegalArgumentException ex) {
                    throw new IllegalArgumentException("Unknown persistent attribute '" + entry.getKey() + "'.", ex);
                }
                if (attribute instanceof jakarta.persistence.metamodel.SingularAttribute<?, ?> singular
                        && singular.isId())
                    throw new IllegalArgumentException("Primary-key attribute '" + entry.getKey() + "' cannot be updated.");
                setAttribute(entity, entry.getKey(), entry.getValue());
            }
            entityManager.flush();
            return 1;
            } finally {
                if (entityManager.contains(entity)) entityManager.detach(entity);
            }
        }, null);
    }

    public int delete(Object id) {
        Objects.requireNonNull(id, "id");
        return inTransaction(() -> {
            T entity = entityManager.find(entityType, id);
            if (entity == null) return 0;
            entityManager.remove(entity);
            entityManager.flush();
            return 1;
        }, null);
    }

    private void setAttribute(T entity, String name, Object value) {
        var attribute = entityManager.getMetamodel().entity(entityType).getAttribute(name);
        Class<?> javaType = attribute.getJavaType();
        if (value != null && !box(javaType).isInstance(value))
            throw new IllegalArgumentException("Value for '" + name + "' must be " + javaType.getName()
                    + ", got " + value.getClass().getName() + ".");
        if (generatedModel != null) {
            if (!generatedModel.canWrite(name))
                throw new IllegalArgumentException("Generated model does not allow writing attribute '" + name + "'.");
            generatedModel.write(entity, name, value);
            return;
        }
        try {
            var field = entityType.getDeclaredField(name);
            if (!field.canAccess(entity)) field.setAccessible(true);
            field.set(entity, value);
        } catch (ReflectiveOperationException ex) {
            throw new IllegalArgumentException("Cannot write persistent attribute '" + name
                    + "'. Use an entity setter or the generated JpaEntityModel access path.", ex);
        }
    }

    private static Class<?> box(Class<?> type) {
        if (!type.isPrimitive()) return type;
        if (type == int.class) return Integer.class;
        if (type == long.class) return Long.class;
        if (type == boolean.class) return Boolean.class;
        if (type == double.class) return Double.class;
        if (type == float.class) return Float.class;
        if (type == short.class) return Short.class;
        if (type == byte.class) return Byte.class;
        if (type == char.class) return Character.class;
        return type;
    }

    private <R> R inTransaction(java.util.concurrent.Callable<R> action, T detachAfter) {
        EntityTransaction tx = entityManager.getTransaction();
        boolean owns = !tx.isActive();
        if (owns) tx.begin();
        try {
            R result = action.call();
            if (owns) tx.commit();
            return result;
        } catch (RuntimeException ex) {
            if (owns && tx.isActive()) tx.rollback();
            else if (!owns) tx.setRollbackOnly();
            throw ex;
        } catch (Exception ex) {
            if (owns && tx.isActive()) tx.rollback();
            else if (!owns) tx.setRollbackOnly();
            throw new IllegalStateException("JPA repository operation failed.", ex);
        } finally {
            if (detachAfter != null && entityManager.contains(detachAfter)) entityManager.detach(detachAfter);
        }
    }
}
