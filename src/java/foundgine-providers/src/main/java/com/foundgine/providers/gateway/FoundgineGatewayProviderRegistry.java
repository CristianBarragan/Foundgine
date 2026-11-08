package com.foundgine.providers.gateway;
import java.time.Instant;
import java.util.Comparator;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;
import java.util.concurrent.ConcurrentHashMap;
/**
 * Thread-safe lifecycle registry. Registration does not instantiate a provider or grant execution authority.
 * A failed lifecycle publication rolls the registry mutation back.
 */
public final class FoundgineGatewayProviderRegistry implements IFoundgineGatewayProviderRegistry {
    private final ConcurrentHashMap<String, FoundgineGatewayProviderStatus> providers = new ConcurrentHashMap<>();
    private final IFoundgineGatewayEventPublisher events;
    public FoundgineGatewayProviderRegistry(IFoundgineGatewayEventPublisher events) {
        this.events = java.util.Objects.requireNonNull(events, "events");
    }
    private static String key(String id) {
        if (id == null || id.isBlank()) throw new IllegalArgumentException("providerId is required");
        return id.toLowerCase(Locale.ROOT);
    }
    @Override public java.util.List<FoundgineGatewayProviderStatus> getSnapshot() {
        return providers.values().stream().sorted(Comparator.comparing(s -> s.provider().id(), String.CASE_INSENSITIVE_ORDER)).toList();
    }
    @Override public Optional<FoundgineGatewayProviderStatus> tryGet(String providerId) {
        return Optional.ofNullable(providers.get(key(providerId)));
    }
    @Override public void register(FoundgineGatewayProviderDescriptor provider) {
        if (provider == null) throw new IllegalArgumentException("provider is required");
        var status = new FoundgineGatewayProviderStatus(provider, FoundgineGatewayProviderHealth.UNKNOWN, Instant.now(), null);
        String id = key(provider.id());
        if (providers.putIfAbsent(id, status) != null) throw new IllegalStateException("Provider '" + provider.id() + "' is already registered");
        try {
            events.publish(FoundgineGatewayEventType.PROVIDER_REGISTERED, provider.id(), null, provider.id(), Map.of());
        } catch (RuntimeException failure) {
            providers.remove(id, status); throw failure;
        }
    }
    @Override public void reportHealth(String providerId, FoundgineGatewayProviderHealth health, String description) {
        String id = key(providerId);
        if (health == null) throw new IllegalArgumentException("health is required");
        final FoundgineGatewayProviderStatus[] previous = new FoundgineGatewayProviderStatus[1];
        final FoundgineGatewayProviderStatus[] updated = new FoundgineGatewayProviderStatus[1];
        providers.compute(id, (ignored, current) -> {
            if (current == null) throw new IllegalStateException("Provider '" + providerId + "' is not registered");
            previous[0] = current;
            updated[0] = new FoundgineGatewayProviderStatus(current.provider(), health, Instant.now(), description);
            return updated[0];
        });
        try {
            events.publish(FoundgineGatewayEventType.PROVIDER_HEALTH_CHANGED, providerId, null, providerId,
                    Map.of("health", health.name(), "description", description == null ? "" : description));
        } catch (RuntimeException failure) {
            providers.replace(id, updated[0], previous[0]); throw failure;
        }
    }
    @Override public boolean unregister(String providerId) {
        String id = key(providerId);
        var removed = providers.remove(id);
        if (removed == null) return false;
        try {
            events.publish(FoundgineGatewayEventType.PROVIDER_UNREGISTERED, providerId, null, providerId, Map.of());
            return true;
        } catch (RuntimeException failure) {
            providers.putIfAbsent(id, removed); throw failure;
        }
    }
}
