package com.foundgine.providers.gateway;
public record FoundgineProviderNegotiationFailure(FoundgineProviderKind kind, long missing, String closestProviderId, String reason) { }
