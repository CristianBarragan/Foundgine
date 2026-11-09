package com.foundgine.extensions.temporal.http;

public final class FoundgineTemporalHttpOptions {
    /** Operator policy the approval endpoint requires (checked through {@code GatewayHttpAuthorizer.satisfiesPolicy}). */
    public String approvalPolicy;
    public int maxPayloadBytes = 64 * 1024;
    public String routePrefix = "/foundgine/workflows";
}
