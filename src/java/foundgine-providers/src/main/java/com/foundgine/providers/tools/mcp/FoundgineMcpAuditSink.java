package com.foundgine.providers.tools.mcp;

/**
 * Receives {@link FoundgineMcpAuditEvent}s. Called synchronously after the audited operation has already taken effect, so keep
 * it fast (hand off to a queue or logger). An exception thrown by a sink is swallowed: a broken audit pipeline must never
 * turn a revocation or a session check into a failure or, worse, undo it. Hosts that need guaranteed delivery should make the
 * sink itself durable and alert on its own failures.
 */
@FunctionalInterface
public interface FoundgineMcpAuditSink {
    void record(FoundgineMcpAuditEvent event);

    FoundgineMcpAuditSink NONE = event -> { };

    /** Delivers to the sink, never throwing. */
    static void emit(FoundgineMcpAuditSink sink, FoundgineMcpAuditEvent event) {
        try { sink.record(event); } catch (RuntimeException ignored) { /* audit must not affect the operation */ }
    }
}
