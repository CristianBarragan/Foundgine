package com.foundgine.providers.outbox;

/** Writer side. Implementations that offer a transactional outbox also expose a way to enlist in the caller's transaction. */
public interface IFoundgineGatewayOutbox {
    /** Idempotent by (tenant, message id). Not atomic with any caller transaction unless the implementation says so. */
    void enqueue(FoundgineOutboxMessage message);
}
