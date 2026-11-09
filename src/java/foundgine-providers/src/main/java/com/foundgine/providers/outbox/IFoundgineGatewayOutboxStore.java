package com.foundgine.providers.outbox;

import java.time.Duration;
import java.time.Instant;
import java.util.List;

/** Dispatcher-facing side: lease, acknowledge, retry or dead-letter. */
public interface IFoundgineGatewayOutboxStore {
    List<FoundgineOutboxLease> lease(int batchSize, Duration leaseDuration, Instant now);

    /** Returns false when the lease was lost (expired and re-leased) or the message is already finished. */
    boolean complete(FoundgineOutboxLease lease, Instant now);

    /** {@code retryAt} is when to try again; {@code null} moves the message to the dead-letter state. */
    boolean fail(FoundgineOutboxLease lease, String error, Instant retryAt, Instant now);
}
