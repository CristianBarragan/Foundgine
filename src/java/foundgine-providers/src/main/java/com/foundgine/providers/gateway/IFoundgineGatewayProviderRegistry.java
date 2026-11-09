package com.foundgine.providers.gateway;
import java.util.List;
import java.util.Optional;
public interface IFoundgineGatewayProviderRegistry {
    List<FoundgineGatewayProviderStatus> getSnapshot();
    Optional<FoundgineGatewayProviderStatus> tryGet(String providerId);
    void register(FoundgineGatewayProviderDescriptor provider);
    void reportHealth(String providerId, FoundgineGatewayProviderHealth health, String description);
    boolean unregister(String providerId);
}
