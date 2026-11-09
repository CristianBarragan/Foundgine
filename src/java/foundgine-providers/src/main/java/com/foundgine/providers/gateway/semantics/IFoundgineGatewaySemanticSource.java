package com.foundgine.providers.gateway.semantics;

/**
 * Read-only bridge from the gateway to Foundgine's canonical semantic capability contract. It deliberately delegates
 * discovery to {@code IFoundgine}; it does not rebuild metadata or introduce a second authorization/discovery
 * implementation.
 */
public interface IFoundgineGatewaySemanticSource {
    FoundgineGatewaySemanticDescription describe();
}
