package com.foundgine.runtime.controlplane.approvals;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.Approvals.ApprovalRequest} (and
 * {@code ApprovalStatus}, {@code ApprovalGrant} from the same C# file).
 *
 * <p>A pending human sign-off for a tool call that policy flagged with
 * {@code PolicyOutcome.RequireApproval}. This is intentionally distinct from
 * {@code Foundgine.Runtime.PlanApproval}: that type binds an execution to an
 * exact plan fingerprint at the moment of execution; this type is the
 * upstream human workflow that decides whether the call may proceed at all.
 */
public final class ApprovalRequest {
    public enum ApprovalStatus {
        PENDING, GRANTED, DENIED, EXPIRED
    }

    public record ApprovalGrant(String approverId, boolean granted, String comment, Instant decidedAt) {
    }

    private final String approvalId;
    private final String requestFingerprint;
    private final int requiredApprovals;
    private final ApprovalStatus status;
    private final List<ApprovalGrant> grants;

    public ApprovalRequest(String approvalId, String requestFingerprint, int requiredApprovals,
                            ApprovalStatus status, List<ApprovalGrant> grants) {
        this.approvalId = Objects.requireNonNull(approvalId, "approvalId");
        this.requestFingerprint = Objects.requireNonNull(requestFingerprint, "requestFingerprint");
        this.requiredApprovals = requiredApprovals;
        this.status = Objects.requireNonNull(status, "status");
        this.grants = List.copyOf(grants);
    }

    public static ApprovalRequest create(String approvalId, String requestFingerprint, int requiredApprovals) {
        if (approvalId == null || approvalId.isBlank()) {
            throw new IllegalArgumentException("approvalId is required.");
        }
        if (requestFingerprint == null || requestFingerprint.isBlank()) {
            throw new IllegalArgumentException("requestFingerprint is required.");
        }
        if (requiredApprovals < 1) {
            throw new IllegalArgumentException("At least one approval must be required.");
        }
        return new ApprovalRequest(approvalId, requestFingerprint, requiredApprovals, ApprovalStatus.PENDING, List.of());
    }

    public static ApprovalRequest create(String approvalId, String requestFingerprint) {
        return create(approvalId, requestFingerprint, 1);
    }

    public String approvalId() {
        return approvalId;
    }

    public String requestFingerprint() {
        return requestFingerprint;
    }

    public int requiredApprovals() {
        return requiredApprovals;
    }

    public ApprovalStatus status() {
        return status;
    }

    public List<ApprovalGrant> grants() {
        return grants;
    }

    public ApprovalRequest withGrant(ApprovalGrant grant) {
        Objects.requireNonNull(grant, "grant");
        if (status != ApprovalStatus.PENDING) {
            throw new IllegalStateException(
                    "Approval request '" + approvalId + "' is already '" + status + "' and cannot accept further decisions.");
        }

        var updatedGrants = new ArrayList<>(grants);
        updatedGrants.add(grant);

        if (!grant.granted()) {
            return new ApprovalRequest(approvalId, requestFingerprint, requiredApprovals, ApprovalStatus.DENIED, updatedGrants);
        }

        long grantedCount = updatedGrants.stream().filter(ApprovalGrant::granted).count();
        var newStatus = grantedCount >= requiredApprovals ? ApprovalStatus.GRANTED : ApprovalStatus.PENDING;
        return new ApprovalRequest(approvalId, requestFingerprint, requiredApprovals, newStatus, updatedGrants);
    }
}
