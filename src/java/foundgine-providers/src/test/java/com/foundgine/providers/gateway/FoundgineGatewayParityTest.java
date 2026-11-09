package com.foundgine.providers.gateway;

import static org.junit.jupiter.api.Assertions.*;
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;

class FoundgineGatewayParityTest {
    private static FoundgineGatewayCapability capability(String id) {
        return new FoundgineGatewayCapability(id, "PostgreSQL event store", "1.0", "storage",
                "Durable ordered events", List.of("append", "replay"), Map.of("scope", "tenant"));
    }

    @Test void gatewayPublishesImmutableDiscoverySnapshotAndFindsCaseInsensitively() {
        var options = new FoundgineGatewayOptions().addCapability(capability("events.postgres"));
        var gateway = new FoundgineGateway(options);
        options.addCapability(capability("later"));
        assertEquals(1, gateway.describe().capabilities().size());
        assertTrue(gateway.findCapability("EVENTS.POSTGRES").isPresent());
        assertTrue(gateway.findCapability("missing").isEmpty());
        assertThrows(UnsupportedOperationException.class, () -> gateway.describe().capabilities().clear());
    }

    @Test void capabilityIdsAreUniqueIgnoringCase() {
        var options = new FoundgineGatewayOptions().addCapability(capability("events"));
        assertThrows(IllegalStateException.class, () -> options.addCapability(capability("EVENTS")));
    }

    @Test void providerRegistrationPublishesAndCanBeDiscovered() {
        var published = new AtomicInteger();
        var registry = new FoundgineGatewayProviderRegistry((type, subject, correlation, provider, attrs) -> published.incrementAndGet());
        var descriptor = new FoundgineGatewayProviderDescriptor("pg.events", "PostgreSQL", "1.0", List.of("DURABLE_STORAGE"));
        registry.register(descriptor);
        assertEquals(1, published.get());
        assertEquals(FoundgineGatewayProviderHealth.UNKNOWN, registry.tryGet("PG.EVENTS").orElseThrow().health());
        assertEquals("pg.events", registry.getSnapshot().getFirst().provider().id());
        assertThrows(IllegalStateException.class, () -> registry.register(descriptor));
    }

    @Test void failedRegistrationPublicationRollsBackRegistryMutation() {
        var registry = new FoundgineGatewayProviderRegistry((type, subject, correlation, provider, attrs) -> {
            throw new IllegalStateException("event sink unavailable");
        });
        var descriptor = new FoundgineGatewayProviderDescriptor("pg.events", "PostgreSQL", "1.0", List.of());
        assertThrows(IllegalStateException.class, () -> registry.register(descriptor));
        assertTrue(registry.tryGet("pg.events").isEmpty());
    }

    @Test void failedHealthPublicationRestoresPreviousStatus() {
        var fail = new java.util.concurrent.atomic.AtomicBoolean(false);
        var registry = new FoundgineGatewayProviderRegistry((type, subject, correlation, provider, attrs) -> {
            if (fail.get()) throw new IllegalStateException("event sink unavailable");
        });
        registry.register(new FoundgineGatewayProviderDescriptor("pg.events", "PostgreSQL", "1.0", List.of()));
        var before = registry.tryGet("pg.events").orElseThrow();
        fail.set(true);
        assertThrows(IllegalStateException.class,
                () -> registry.reportHealth("pg.events", FoundgineGatewayProviderHealth.UNHEALTHY, "offline"));
        assertEquals(before, registry.tryGet("pg.events").orElseThrow());
    }

    @Test void failedUnregisterPublicationRestoresProvider() {
        var fail = new java.util.concurrent.atomic.AtomicBoolean(false);
        var registry = new FoundgineGatewayProviderRegistry((type, subject, correlation, provider, attrs) -> {
            if (fail.get() && type == FoundgineGatewayEventType.PROVIDER_UNREGISTERED)
                throw new IllegalStateException("event sink unavailable");
        });
        registry.register(new FoundgineGatewayProviderDescriptor("pg.events", "PostgreSQL", "1.0", List.of()));
        fail.set(true);
        assertThrows(IllegalStateException.class, () -> registry.unregister("pg.events"));
        assertTrue(registry.tryGet("pg.events").isPresent());
    }

    private record TestProvider(String id, FoundgineProviderKind kind, long capabilities) implements IFoundgineProvider {
        public String name() { return id; } public String version() { return "1.0"; }
    }

    @Test void negotiationRequiresOneProviderToSatisfyWholeRequirement() {
        var providers = List.of(
                new TestProvider("pg", FoundgineProviderKind.EVENT_STORE,
                        FoundgineProviderCapability.mask(FoundgineProviderCapability.DURABLE_STORAGE)),
                new TestProvider("kafka", FoundgineProviderKind.EVENT_STORE,
                        FoundgineProviderCapability.mask(FoundgineProviderCapability.ORDERED_EVENTS)));
        var requirement = new FoundgineProviderRequirement(FoundgineProviderKind.EVENT_STORE,
                FoundgineProviderCapability.mask(FoundgineProviderCapability.DURABLE_STORAGE,
                        FoundgineProviderCapability.ORDERED_EVENTS), "journal must be durable and ordered");
        var result = FoundgineProviderNegotiator.negotiate(providers, List.of(requirement));
        assertFalse(result.isSatisfied());
        assertEquals(1, result.failures().size());
        assertThrows(FoundgineProviderNegotiationException.class, result::throwIfUnsatisfied);
    }

    @Test void negotiationAcceptsProviderWithAllRequiredCapabilities() {
        var provider = new TestProvider("pg", FoundgineProviderKind.EVENT_STORE,
                FoundgineProviderCapability.mask(FoundgineProviderCapability.DURABLE_STORAGE,
                        FoundgineProviderCapability.ORDERED_EVENTS));
        var requirement = new FoundgineProviderRequirement(FoundgineProviderKind.EVENT_STORE,
                FoundgineProviderCapability.mask(FoundgineProviderCapability.DURABLE_STORAGE,
                        FoundgineProviderCapability.ORDERED_EVENTS), null);
        assertTrue(FoundgineProviderNegotiator.negotiate(List.of(provider), List.of(requirement)).isSatisfied());
    }
}
