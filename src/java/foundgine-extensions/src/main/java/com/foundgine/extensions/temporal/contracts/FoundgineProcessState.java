package com.foundgine.extensions.temporal.contracts;

public final class FoundgineProcessState {
    public static final String RUNNING = "Running";
    public static final String AWAITING_APPROVAL = "AwaitingApproval";
    public static final String COMPENSATING = "Compensating";
    public static final String COMPLETED = "Completed";
    public static final String FAILED = "Failed";
    public static final String CANCELLED = "Cancelled";
    public static final String REJECTED = "Rejected";

    private FoundgineProcessState() { }
}
