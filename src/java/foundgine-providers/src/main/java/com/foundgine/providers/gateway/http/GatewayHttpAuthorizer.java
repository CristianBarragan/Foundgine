package com.foundgine.providers.gateway.http;

/**
 * Host-supplied authentication/authorization, the framework-neutral counterpart of ASP.NET's
 * {@code RequireAuthorization()}. The gateway never authenticates callers itself.
 */
public interface GatewayHttpAuthorizer {
    enum Decision { ALLOWED, UNAUTHENTICATED, FORBIDDEN }

    Decision authorize(GatewayHttpRequest request);

    /** Whether the caller satisfies the named operator policy (see {@code FoundgineGatewayEventStreamOptions.operatorPolicy}). */
    default boolean satisfiesPolicy(GatewayHttpRequest request, String policy) { return false; }
}
