package com.foundgine.runtime.controlplane.auditlog;

import java.time.Instant;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.AuditLog.AuditEvent}, together
 * with the {@code AuditCategory} enum declared in the same C# file.
 *
 * <p>An immutable record of one governance step for one tool call. Events
 * carry a fingerprint rather than raw request/result payloads, the same
 * evidence-not-payload discipline used by
 * {@code com.foundgine.core.execution.ExecutionReceipt}, so the audit log
 * can be retained and shipped without duplicating domain data.
 *
 * <p><b>Porting decision:</b> {@code DateTimeOffset} is ported as
 * {@link Instant}, consistent with other timestamp fields elsewhere in this
 * port (e.g. {@code ApprovalRequest.ApprovalGrant.decidedAt}).
 */
public record AuditEvent(
        AuditCategory category,
        String toolName,
        String actor,
        String tenant,
        String fingerprint,
        String summary,
        Instant occurredAt) {

    public enum AuditCategory {
        RISK_SCORED,
        POLICY_EVALUATED,
        APPROVAL_REQUESTED,
        APPROVAL_DECIDED,
        ROUTED,
        DENIED,
    }

    public static AuditEvent create(
            AuditCategory category,
            String toolName,
            String actor,
            String tenant,
            String fingerprint,
            String summary) {
        return new AuditEvent(category, toolName, actor, tenant, fingerprint, summary, Instant.now());
    }
}
