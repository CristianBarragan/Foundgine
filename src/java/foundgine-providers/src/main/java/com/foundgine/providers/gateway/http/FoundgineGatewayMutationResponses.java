package com.foundgine.providers.gateway.http;

import java.time.Instant;
import java.util.List;

/** Wire responses for the mutation endpoints. */
public final class FoundgineGatewayMutationResponses {
    private FoundgineGatewayMutationResponses() { }

    public record DryRun(String planFingerprint, List<PlanOperation> operations, List<String> effects) { }
    public record PlanOperation(int index, String entity, String kind, List<String> fields, List<String> returnFields) { }
    public record Approval(String handle, Instant expiresAt, String planFingerprint) { }
    public record Execution(String planFingerprint, String resultFingerprint, String approvalId, String approvedBy,
            int operationCount, int totalAffectedRows) { }
}
