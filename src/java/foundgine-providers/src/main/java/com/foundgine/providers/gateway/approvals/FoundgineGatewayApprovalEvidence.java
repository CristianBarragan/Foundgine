package com.foundgine.providers.gateway.approvals;

import java.time.Instant;
import java.util.Objects;

/** Non-secret evidence committed with an approval and its outbox notification. */
public record FoundgineGatewayApprovalEvidence(String eventId, String eventType, String subject,
        String tenant, String planFingerprint, Instant occurredAt, Instant expiresAt) {
    public FoundgineGatewayApprovalEvidence {
        Objects.requireNonNull(eventId, "eventId");
        Objects.requireNonNull(eventType, "eventType");
        Objects.requireNonNull(subject, "subject");
        Objects.requireNonNull(planFingerprint, "planFingerprint");
        Objects.requireNonNull(occurredAt, "occurredAt");
        Objects.requireNonNull(expiresAt, "expiresAt");
    }
}
