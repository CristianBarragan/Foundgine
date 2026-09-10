package com.foundgine.runtime;

import com.foundgine.core.semantic.SemanticRequest;

import java.time.Instant;

/**
 * Port of {@code Foundgine.Runtime.PlanApproval}.
 *
 * <p>Approval bound to the exact authorized semantic plan represented by the
 * fingerprint. It is not an authorization grant and cannot be reused for a
 * different plan.
 *
 * <p>C#'s {@code DateTimeOffset} is ported as {@link Instant}, matching this
 * port's established convention.
 */
public record PlanApproval(
        SemanticRequest request,
        String approvalId,
        String planFingerprint,
        String semanticModelVersion,
        int capabilityContractVersion,
        int capabilityVersion,
        int intentVersion,
        int planVersion,
        String approvedBy,
        Instant approvedAt) {
}
