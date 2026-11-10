package com.foundgine.runtime.controlplane.auditlog;

/** Durable append-only sink. Returning from append means persistence was acknowledged. */
@FunctionalInterface
public interface AuditEventStore {
    void append(AuditEvent event) throws Exception;
}
