package com.foundgine.providers.gateway.events;
/** The caller as the event stream sees it. Digests come from {@link FoundgineGatewayScopeDigester}. */
public record FoundgineGatewayViewer(String ownerDigest, String tenantDigest, boolean isOperator) { }
