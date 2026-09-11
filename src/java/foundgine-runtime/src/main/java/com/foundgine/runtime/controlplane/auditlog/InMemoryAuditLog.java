package com.foundgine.runtime.controlplane.auditlog;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.AuditLog.InMemoryAuditLog},
 * declared alongside {@code IAuditLog} in the same C# file.
 *
 * <p>Process-local, append-only audit log. Entries are never mutated or
 * removed once recorded. A deployment that needs durable or tamper-evident
 * audit history should back {@link IAuditLog} with external storage — the
 * append-only contract is what callers can rely on either way.
 *
 * <p><b>Porting decision:</b> as with {@code InMemoryApprovalStore}, the C#
 * {@code System.Threading.Lock} is ported as a plain {@code Object} monitor
 * guarded with {@code synchronized}. The C# {@code Where(...).ToArray()}
 * filter chain is ported as an explicit loop over a defensive copy, avoiding
 * a stream pipeline for a simple two-predicate filter while keeping the
 * snapshot-under-lock semantics identical.
 */
public final class InMemoryAuditLog implements IAuditLog {
    private final List<AuditEvent> events = new ArrayList<>();
    private final Object gate = new Object();

    @Override
    public void record(AuditEvent auditEvent) {
        Objects.requireNonNull(auditEvent, "auditEvent");
        synchronized (gate) {
            events.add(auditEvent);
        }
    }

    @Override
    public List<AuditEvent> query(String toolName, String tenant) {
        synchronized (gate) {
            var result = new ArrayList<AuditEvent>(events.size());
            for (var event : events) {
                if (toolName != null && !toolName.isBlank() && !toolName.equals(event.toolName())) {
                    continue;
                }
                if (tenant != null && !tenant.isBlank() && !tenant.equals(event.tenant())) {
                    continue;
                }
                result.add(event);
            }
            return List.copyOf(result);
        }
    }
}
