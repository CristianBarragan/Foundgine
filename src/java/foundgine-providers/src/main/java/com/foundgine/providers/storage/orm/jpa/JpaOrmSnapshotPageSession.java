package com.foundgine.providers.storage.orm.jpa;

import com.foundgine.providers.storage.orm.IOrmQuerySource;

import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.EntityTransaction;

import java.util.Objects;

/**
 * Owns one resource-local EntityManager transaction for a multi-page read.
 *
 * <p>The caller must configure the database transaction isolation to provide a repeatable
 * snapshot (or stronger) and explicitly acknowledge that configuration when opening the
 * session. JPA has no portable API for setting or verifying transaction isolation, so this
 * class deliberately does not claim to configure or prove the database guarantee.
 *
 * <p>All pages in the session must use {@link #querySource()}. Do not mix this source with an
 * EntityManagerFactory-backed source, which creates a new EntityManager per fetch.
 */
public final class JpaOrmSnapshotPageSession implements AutoCloseable {
    /** Explicit caller assertion about the datasource/transaction isolation configuration. */
    public enum IsolationGuarantee {
        REPEATABLE_READ_OR_STRONGER
    }

    private final EntityManager entityManager;
    private final EntityTransaction transaction;
    private final IOrmQuerySource querySource;
    private boolean completed;

    private JpaOrmSnapshotPageSession(EntityManager entityManager) {
        this.entityManager = entityManager;
        this.transaction = entityManager.getTransaction();
        this.querySource = new JpaOrmQuerySource(entityManager);
    }

    /**
     * Opens a dedicated EntityManager and begins a transaction for the complete page sequence.
     * The isolation argument is an explicit assertion by the caller; JPA cannot verify it.
     */
    public static JpaOrmSnapshotPageSession begin(
            EntityManagerFactory factory, IsolationGuarantee isolationGuarantee) {
        Objects.requireNonNull(factory, "factory");
        Objects.requireNonNull(isolationGuarantee, "isolationGuarantee");
        EntityManager em = factory.createEntityManager();
        try {
            EntityTransaction tx = em.getTransaction();
            if (tx.isActive()) {
                throw new IllegalStateException("A newly created EntityManager unexpectedly has an active transaction.");
            }
            tx.begin();
            if (!tx.isActive()) {
                throw new IllegalStateException("JPA did not start the snapshot page transaction.");
            }
            return new JpaOrmSnapshotPageSession(em);
        } catch (RuntimeException ex) {
            if (em.isOpen()) em.close();
            throw ex;
        }
    }

    /** The one query source that must be reused for every page in this snapshot session. */
    public IOrmQuerySource querySource() {
        ensureActive();
        return querySource;
    }

    /** Commits and closes the read transaction after all pages have been consumed. */
    public void commit() {
        ensureActive();
        try {
            transaction.commit();
            completed = true;
        } finally {
            if (entityManager.isOpen()) entityManager.close();
        }
    }

    /** Rolls back and closes the page transaction. */
    public void rollback() {
        ensureActive();
        try {
            if (transaction.isActive()) transaction.rollback();
            completed = true;
        } finally {
            if (entityManager.isOpen()) entityManager.close();
        }
    }

    private void ensureActive() {
        if (completed || !entityManager.isOpen() || !transaction.isActive()) {
            throw new IllegalStateException("Snapshot page session is no longer active.");
        }
    }

    /** Closing an unfinished session rolls it back; it never silently commits a partial export. */
    @Override
    public void close() {
        if (completed) return;
        try {
            if (entityManager.isOpen() && transaction.isActive()) transaction.rollback();
            completed = true;
        } finally {
            if (entityManager.isOpen()) entityManager.close();
        }
    }
}
