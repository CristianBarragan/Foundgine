package com.foundgine.providers.gateway;
import java.time.Instant;
public record FoundgineGatewayProviderStatus(FoundgineGatewayProviderDescriptor provider,
        FoundgineGatewayProviderHealth health, Instant updatedAt, String description) { }
