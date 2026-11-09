package com.foundgine.providers.gateway.approvals;

import com.foundgine.runtime.MutationPlanApproval;
import java.time.Instant;
import java.util.Optional;

/** Stores sensitive runtime approvals server-side. Implementations must provide atomic take/revoke semantics. */
public interface IFoundgineGatewayApprovalStore {
    void store(String handleDigest, MutationPlanApproval approval, String subject, String tenant, Instant expiresAt);
    /** Atomically removes and returns an approval only when owner, tenant and expiry match. */
    Optional<MutationPlanApproval> take(String handleDigest, String subject, String tenant, Instant now);
    boolean revoke(String handleDigest, String subject, String tenant);
}
