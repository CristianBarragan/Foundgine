package com.foundgine.extensions.temporal.contracts;

public record FoundgineApprovalDecision(String step, boolean approved, String reason, String decidedBy) {
    public FoundgineApprovalDecision(String step, boolean approved) { this(step, approved, null, null); }
}
