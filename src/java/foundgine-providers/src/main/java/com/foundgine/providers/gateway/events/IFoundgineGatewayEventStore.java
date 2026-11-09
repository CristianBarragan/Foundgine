package com.foundgine.providers.gateway.events;

import com.foundgine.providers.gateway.FoundgineGatewayEvent;
import java.util.List;

/**
 * Append-only gateway event journal. Production implementations should use a durable store and guarantee
 * monotonically increasing sequence values plus atomic append semantics.
 */
public interface IFoundgineGatewayEventStore {
    /** Appends the event and returns it with its assigned sequence. */
    FoundgineGatewayEvent append(FoundgineGatewayEvent gatewayEvent);
    long getLatestSequence();
    /** Events with sequence greater than {@code sequence}, ascending, at most {@code limit} (1-10,000). */
    List<FoundgineGatewayEvent> readAfter(long sequence, int limit);
    /** The most recent {@code limit} events carrying {@code correlationId}, ascending. */
    List<FoundgineGatewayEvent> readByCorrelationId(String correlationId, int limit);
}
