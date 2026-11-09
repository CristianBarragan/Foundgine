package com.foundgine.providers.gateway.audit;

import com.foundgine.providers.gateway.FoundgineGatewayEvent;

/**
 * Durable audit persistence boundary. Implementations should append an immutable, versioned record
 * to the application's audit store and enforce its retention/integrity policy.
 */
@FunctionalInterface
public interface IFoundgineGatewayAuditSink {
    void append(FoundgineGatewayEvent gatewayEvent) throws Exception;
}
