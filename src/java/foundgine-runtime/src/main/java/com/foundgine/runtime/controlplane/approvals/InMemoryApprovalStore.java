package com.foundgine.runtime.controlplane.approvals;

import java.util.HashMap;
import java.util.Map;
import java.util.NoSuchElementException;
import java.util.Objects;
import java.util.Optional;
import java.util.UUID;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.Approvals.InMemoryApprovalStore},
 * declared alongside {@code IApprovalStore} in the same C# file.
 *
 * <p>Process-local approval store. A production deployment with real
 * human-in-the-loop approvers should back {@link IApprovalStore} with
 * durable storage — pending approvals must survive a process restart —
 * but the interface and workflow shape stay the same.
 *
 * <p><b>Porting decision:</b> the C# type synchronizes on a {@code System.Threading.Lock}
 * (.NET 9's dedicated lock type). Java has no equivalent lock type, so a
 * private {@code final Object} monitor is used with {@code synchronized}
 * blocks instead, which is the standard Java substitute for a plain mutual-exclusion lock.
 * {@code Guid.NewGuid().ToString("n")} (32 lowercase hex digits, no dashes) is
 * ported as a dash-stripped, lowercased {@link UUID#randomUUID()}.
 */
public final class InMemoryApprovalStore implements IApprovalStore {
    private final Map<String, ApprovalRequest> requests = new HashMap<>();
    private final Object gate = new Object();

    @Override
    public ApprovalRequest create(String requestFingerprint, int requiredApprovals) {
        var approvalId = UUID.randomUUID().toString().replace("-", "");
        var request = ApprovalRequest.create(approvalId, requestFingerprint, requiredApprovals);
        synchronized (gate) {
            requests.put(request.approvalId(), request);
        }
        return request;
    }

    @Override
    public Optional<ApprovalRequest> tryGet(String approvalId) {
        if (approvalId == null || approvalId.isBlank()) {
            throw new IllegalArgumentException("approvalId is required.");
        }
        synchronized (gate) {
            return Optional.ofNullable(requests.get(approvalId));
        }
    }

    @Override
    public ApprovalRequest recordDecision(String approvalId, ApprovalRequest.ApprovalGrant grant) {
        if (approvalId == null || approvalId.isBlank()) {
            throw new IllegalArgumentException("approvalId is required.");
        }
        Objects.requireNonNull(grant, "grant");

        synchronized (gate) {
            var existing = requests.get(approvalId);
            if (existing == null) {
                throw new NoSuchElementException("No approval request '" + approvalId + "' exists.");
            }

            var updated = existing.withGrant(grant);
            requests.put(approvalId, updated);
            return updated;
        }
    }
}
