package com.foundgine.providers.gateway.http;

/** HTTP input bounds. Hosts should also configure their server's global request-body limits. */
public final class FoundgineGatewayHttpOptions {
    public long maxRequestBytes = 256 * 1024;
    public int defaultReadLimit = 100;
    public int maxReadLimit = 1000;
    public int maxSelectionDepth = 8;
    public int maxSelectionCount = 128;
    public int maxFilterDepth = 8;
    public int maxFilterNodes = 128;
    public int maxJsonValueDepth = 8;
    public int maxMutationOperations = 32;
    public int maxMutationFields = 128;
    public int maxMutationFilterDepth = 8;
    public int maxMutationFilterNodes = 128;

    void validateReadLimits() {
        if (maxRequestBytes < 1 || maxSelectionDepth < 1 || maxSelectionCount < 1 || maxFilterDepth < 1
                || maxFilterNodes < 1 || maxJsonValueDepth < 1 || defaultReadLimit < 1 || maxReadLimit < defaultReadLimit)
            throw new IllegalStateException("Gateway HTTP limits must all be positive.");
    }
}
