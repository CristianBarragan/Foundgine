package com.foundgine.providers.storage.orm;

import java.util.List;

/**
 * Write-side persistence contract (entities are managed objects). Used only by {@link
 * OrmMutationExecutionProvider}.
 */
public interface IOrmMutationSource {

    /** Starts (or joins) the transaction that makes a whole batch atomic. */
    Transaction begin();

    /** Managed entities matching {@code where}. */
    List<Object> find(OrmEntityMapping map, OrmPredicate where);

    void persist(OrmEntityMapping map, Object entity);

    void remove(OrmEntityMapping map, Object entity);

    /** Writes pending changes so generated keys are populated and constraints are checked. */
    void flush();

    /** Closing without {@link #commit()} rolls back. */
    interface Transaction extends AutoCloseable {
        void commit();

        void rollback();

        @Override
        void close();
    }
}
