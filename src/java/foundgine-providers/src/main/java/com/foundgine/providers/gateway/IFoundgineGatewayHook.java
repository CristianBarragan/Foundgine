package com.foundgine.providers.gateway;

/** Receives gateway events for integrations such as audit forwarding, cache invalidation and broker fan-out. */
@FunctionalInterface
public interface IFoundgineGatewayHook {
    void onEvent(FoundgineGatewayEvent gatewayEvent) throws Exception;
}
