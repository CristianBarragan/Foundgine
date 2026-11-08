package com.foundgine.providers.gateway;

import static org.junit.jupiter.api.Assertions.*;

import java.util.List;
import org.junit.jupiter.api.Test;

class ProviderNegotiationParityTest {
    private record Provider(String id, FoundgineProviderKind kind, long capabilities) implements IFoundgineProvider {
        @Override public String name() { return id; }
        @Override public String version() { return "1"; }
    }

    @Test void capabilitiesFromDifferentProvidersAreNeverCombined() {
        var durableOnly = new Provider("durable", FoundgineProviderKind.OUTBOX,
                FoundgineProviderCapability.DURABLE_STORAGE.bit());
        var retryOnly = new Provider("retry", FoundgineProviderKind.OUTBOX,
                FoundgineProviderCapability.RETRY.bit());
        var requirement = new FoundgineProviderRequirement(FoundgineProviderKind.OUTBOX,
                FoundgineProviderCapability.mask(FoundgineProviderCapability.DURABLE_STORAGE,
                        FoundgineProviderCapability.RETRY), "transactional retryable outbox");

        var result = FoundgineProviderNegotiator.negotiate(List.of(durableOnly, retryOnly), List.of(requirement));

        assertFalse(result.isSatisfied());
        assertEquals(1, result.failures().size());
        assertEquals(FoundgineProviderCapability.RETRY.bit(), result.failures().getFirst().missing());
        assertEquals("durable", result.failures().getFirst().closestProviderId());
    }

    @Test void duplicateProviderIdsAreRejectedWithoutCaseSensitivity() {
        var first = new Provider("Postgres", FoundgineProviderKind.EVENT_STORE, 0);
        var second = new Provider("postgres", FoundgineProviderKind.EVENT_STORE, 0);
        assertThrows(IllegalArgumentException.class,
                () -> FoundgineProviderNegotiator.negotiate(List.of(first, second), List.of()));
    }

    @Test void failedStartupDoesNotPublishAnyProviderToTheRegistry() {
        var events = new java.util.ArrayList<FoundgineGatewayEventType>();
        var registry = new FoundgineGatewayProviderRegistry((type, subject, correlation, provider, attributes) -> events.add(type));
        var provider = new Provider("volatile", FoundgineProviderKind.EVENT_STORE,
                FoundgineProviderCapability.ORDERED_EVENTS.bit());
        var requirement = new FoundgineProviderRequirement(FoundgineProviderKind.EVENT_STORE,
                FoundgineProviderCapability.DURABLE_STORAGE.bit(), "durable journal required");

        assertThrows(FoundgineProviderNegotiationException.class,
                () -> FoundgineProviderStartup.validateAndRegister(List.of(provider), List.of(requirement), null, registry));
        assertTrue(registry.getSnapshot().isEmpty());
        assertTrue(events.isEmpty());
    }
}
