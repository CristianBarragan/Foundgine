package com.foundgine.providers.gateway;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.providers.gateway.audit.FoundgineGatewayAuditHook;
import com.foundgine.providers.gateway.diagnostics.FoundgineGatewayDiagnostics;
import com.foundgine.providers.gateway.events.*;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

class FoundgineGatewayEventPipelineParityTest {
    private static final Duration WAIT = Duration.ofMillis(200);

    private static FoundgineGatewayEvent next(FoundgineGatewaySubscription s) throws Exception {
        return s.poll(WAIT).orElseThrow(() -> new AssertionError("expected an event"));
    }

    @Test void publishAssignsMonotonicSequencesAndReplaysThenFollowsLive() throws Exception {
        var hub = new FoundgineGatewayEventHub(new FoundgineGatewayOptions());
        hub.publish(FoundgineGatewayEventType.CUSTOM, "a", null, null, Map.of());
        hub.publish(FoundgineGatewayEventType.CUSTOM, "b", null, null, Map.of());
        try (var sub = hub.subscribe()) {
            assertEquals(1, next(sub).sequence());
            assertEquals(2, next(sub).sequence());
            assertTrue(sub.poll(Duration.ofMillis(20)).isEmpty());
            hub.publish(FoundgineGatewayEventType.CUSTOM, "c", null, null, Map.of());
            assertEquals("c", next(sub).subject());
        }
        assertEquals(0, hub.subscriberCount());
    }

    @Test void subscribeAfterSkipsAlreadySeenEvents() throws Exception {
        var hub = new FoundgineGatewayEventHub(new FoundgineGatewayOptions());
        for (var s : List.of("a", "b", "c")) hub.publish(FoundgineGatewayEventType.CUSTOM, s, null, null, Map.of());
        try (var sub = hub.subscribeAfter(2)) { assertEquals("c", next(sub).subject()); }
    }

    @Test void cursorAheadOfJournalRestartsReplayFromZero() throws Exception {
        var hub = new FoundgineGatewayEventHub(new FoundgineGatewayOptions());
        hub.publish(FoundgineGatewayEventType.CUSTOM, "a", null, null, Map.of());
        try (var sub = hub.subscribeAfter(9_999)) { assertEquals("a", next(sub).subject()); }
    }

    @Test void replayIsPagedAndUnbounded() throws Exception {
        var options = new FoundgineGatewayOptions().replayPageSize(3);
        var hub = new FoundgineGatewayEventHub(options);
        for (int i = 0; i < 10; i++) hub.publish(FoundgineGatewayEventType.CUSTOM, "e" + i, null, null, Map.of());
        try (var sub = hub.subscribe()) {
            for (int i = 1; i <= 10; i++) assertEquals(i, next(sub).sequence());
        }
    }

    @Test void slowSubscriberOverflowIsBackfilledFromJournal() throws Exception {
        var options = new FoundgineGatewayOptions().subscriberBufferCapacity(2);
        var hub = new FoundgineGatewayEventHub(options);
        try (var sub = hub.subscribe()) {
            assertTrue(sub.poll(Duration.ofMillis(10)).isEmpty()); // replay finished, now live
            for (int i = 0; i < 6; i++) hub.publish(FoundgineGatewayEventType.CUSTOM, "e" + i, null, null, Map.of());
            // buffer kept only the newest two (5,6); 1-4 must come from the journal
            for (int i = 1; i <= 6; i++) assertEquals(i, next(sub).sequence());
        }
    }

    @Test void criticalHookFailureBlocksJournalHooksAndSubscribers() throws Exception {
        var store = new InMemoryFoundgineGatewayEventStore();
        var bestEffort = new ArrayList<FoundgineGatewayEvent>();
        var audit = new FoundgineGatewayAuditHook(e -> { throw new IllegalStateException("audit down"); });
        var hub = new FoundgineGatewayEventHub(new FoundgineGatewayOptions(),
                List.of(audit, (IFoundgineGatewayHook) bestEffort::add), store);
        try (var sub = hub.subscribe()) {
            assertThrows(IllegalStateException.class,
                    () -> hub.publish(FoundgineGatewayEventType.CUSTOM, "x", null, null, Map.of()));
            assertEquals(0, store.getLatestSequence());
            assertTrue(bestEffort.isEmpty());
            assertTrue(sub.poll(Duration.ofMillis(20)).isEmpty());
        }
    }

    @Test void checkedCriticalFailureIsWrappedNotSwallowed() {
        IFoundgineGatewayCriticalHook hook = e -> { throw new Exception("checked"); };
        var hub = new FoundgineGatewayEventHub(new FoundgineGatewayOptions(), List.of(hook), null);
        var ex = assertThrows(IllegalStateException.class,
                () -> hub.publish(FoundgineGatewayEventType.CUSTOM, "x", null, null, Map.of()));
        assertEquals("checked", ex.getCause().getMessage());
    }

    @Test void bestEffortHookFailureIsIsolatedAndCounted() throws Exception {
        FoundgineGatewayDiagnostics.reset();
        IFoundgineGatewayHook broken = e -> { throw new RuntimeException("broker down"); };
        var hub = new FoundgineGatewayEventHub(new FoundgineGatewayOptions(), List.of(broken), null);
        try (var sub = hub.subscribe()) {
            hub.publish(FoundgineGatewayEventType.CUSTOM, "x", null, null, Map.of());
            assertEquals(1, next(sub).sequence());
        }
        var snap = FoundgineGatewayDiagnostics.snapshot();
        assertTrue(snap.keySet().stream().anyMatch(k -> k.startsWith(FoundgineGatewayDiagnostics.HOOK_FAILURES)
                && k.contains("hook.critical=false")));
    }

    @Test void criticalHooksRunBeforeJournalAppend() {
        var store = new InMemoryFoundgineGatewayEventStore();
        var seenSequence = new ArrayList<Long>();
        IFoundgineGatewayCriticalHook critical = e -> seenSequence.add(e.sequence());
        new FoundgineGatewayEventHub(new FoundgineGatewayOptions(), List.of(critical), store)
                .publish(FoundgineGatewayEventType.CUSTOM, "x", null, null, Map.of());
        assertEquals(List.of(0L), seenSequence); // not yet sequenced when the audit sink sees it
        assertEquals(1, store.getLatestSequence());
    }

    @Test void inMemoryStoreEnforcesRetentionAndLimits() {
        var store = new InMemoryFoundgineGatewayEventStore(100);
        for (int i = 0; i < 150; i++)
            store.append(FoundgineGatewayEvent.create(FoundgineGatewayEventType.CUSTOM, "e", i % 2 == 0 ? "c" : null, null, null));
        assertEquals(150, store.getLatestSequence());
        assertEquals(51, store.readAfter(0, 10_000).getFirst().sequence());
        assertEquals(100, store.readAfter(0, 10_000).size());
        assertEquals(3, store.readByCorrelationId("c", 3).size());
        assertThrows(IllegalArgumentException.class, () -> store.readAfter(0, 0));
        assertThrows(IllegalArgumentException.class, () -> store.readAfter(0, 10_001));
        assertThrows(IllegalArgumentException.class, () -> new InMemoryFoundgineGatewayEventStore(99));
    }

    @Test void cursorPolicy() {
        assertEquals(FoundgineGatewayCursorStatus.LIVE, FoundgineGatewayEventCursor.resolve("", 50, 10).status());
        assertEquals(50, FoundgineGatewayEventCursor.resolve(null, 50, 10).after());
        assertEquals(FoundgineGatewayCursorStatus.INVALID, FoundgineGatewayEventCursor.resolve("-1", 50, 10).status());
        assertEquals(FoundgineGatewayCursorStatus.INVALID, FoundgineGatewayEventCursor.resolve("1x", 50, 10).status());
        assertEquals(FoundgineGatewayCursorStatus.INVALID, FoundgineGatewayEventCursor.resolve("99999999999999999999", 50, 10).status());
        assertEquals(FoundgineGatewayCursorStatus.REPLAY, FoundgineGatewayEventCursor.resolve("45", 50, 10).status());
        assertEquals(FoundgineGatewayCursorStatus.TOO_OLD, FoundgineGatewayEventCursor.resolve("10", 50, 10).status());
        assertEquals(50, FoundgineGatewayEventCursor.resolve("10", 50, 10).after());
        // cursor ahead of journal -> lag measured against latest, replay restarts
        assertEquals(FoundgineGatewayCursorStatus.REPLAY, FoundgineGatewayEventCursor.resolve("500", 5, 10).status());
    }

    private static FoundgineGatewayEvent event(FoundgineGatewayEventType t, Map<String, String> attrs) {
        return FoundgineGatewayEvent.create(t, "s", null, null, attrs);
    }

    @Test void visibilityRulesAreFailClosed() {
        var d = new FoundgineGatewayScopeDigester(new FoundgineGatewayEventStreamOptions());
        var alice = new FoundgineGatewayViewer(d.owner("t1", "alice"), d.tenant("t1"), false);
        var op = new FoundgineGatewayViewer(d.owner("t2", "ops"), d.tenant("t2"), true);
        var owned = event(FoundgineGatewayEventType.EXECUTION_COMPLETED,
                Map.of(FoundgineGatewayEventAttributes.OWNER_DIGEST, d.owner("t1", "alice")));
        var tenantScoped = event(FoundgineGatewayEventType.CUSTOM,
                Map.of(FoundgineGatewayEventAttributes.TENANT_DIGEST, d.tenant("t1")));
        var discovery = event(FoundgineGatewayEventType.CAPABILITY_CHANGED, Map.of());
        var system = event(FoundgineGatewayEventType.PROVIDER_HEALTH_CHANGED, Map.of());
        assertTrue(FoundgineGatewayEventVisibility.isVisible(owned, alice));
        assertFalse(FoundgineGatewayEventVisibility.isVisible(owned, op));          // operators don't cross tenants
        assertTrue(FoundgineGatewayEventVisibility.isVisible(tenantScoped, alice));
        assertFalse(FoundgineGatewayEventVisibility.isVisible(tenantScoped, op));
        assertTrue(FoundgineGatewayEventVisibility.isVisible(discovery, alice));
        assertFalse(FoundgineGatewayEventVisibility.isVisible(system, alice));
        assertTrue(FoundgineGatewayEventVisibility.isVisible(system, op));
    }

    @Test void toPublicStripsReservedAttributesOnly() {
        var e = event(FoundgineGatewayEventType.CUSTOM, Map.of(FoundgineGatewayEventAttributes.OWNER_DIGEST, "X", "k", "v"));
        var pub = FoundgineGatewayEventVisibility.toPublic(e);
        assertEquals(Map.of("k", "v"), pub.attributes());
        assertEquals(e.eventId(), pub.eventId());
        var plain = event(FoundgineGatewayEventType.CUSTOM, Map.of("k", "v"));
        assertSame(plain, FoundgineGatewayEventVisibility.toPublic(plain));
    }

    @Test void scopeDigestsAreStableKeyedAndDomainSeparated() {
        var unkeyed = new FoundgineGatewayScopeDigester(new FoundgineGatewayEventStreamOptions());
        // legacy unkeyed owner digest = SHA-256("t1\0alice"), upper-case hex
        assertEquals(64, unkeyed.owner("t1", "alice").length());
        assertEquals(unkeyed.owner("t1", "alice"), unkeyed.owner("t1", "alice"));
        assertNotEquals(unkeyed.owner("t1", "alice"), unkeyed.owner("t2", "alice"));
        assertNotEquals(unkeyed.owner("", "x"), unkeyed.tenant(""));
        var opts = new FoundgineGatewayEventStreamOptions().setScopeDigestKey(new byte[32]);
        var keyed = new FoundgineGatewayScopeDigester(opts);
        assertNotEquals(unkeyed.owner("t1", "alice"), keyed.owner("t1", "alice"));
        assertThrows(IllegalArgumentException.class,
                () -> new FoundgineGatewayEventStreamOptions().setScopeDigestKey(new byte[31]));
        assertEquals(unkeyed.owner(null, "alice"), unkeyed.owner("", "alice"));
    }

    @Test void startupValidatorNegotiatesAndRegistersJournal() {
        var store = new InMemoryFoundgineGatewayEventStore();
        var registry = new FoundgineGatewayProviderRegistry((t, s, c, p, a) -> { });
        FoundgineProviderStartup.validateAndRegister(List.of(),
                List.of(new FoundgineProviderRequirement(FoundgineProviderKind.EVENT_STORE,
                        FoundgineProviderCapability.mask(FoundgineProviderCapability.ORDERED_EVENTS), "journal")),
                store, registry);
        assertTrue(registry.tryGet("foundgine.inmemory.eventstore").isPresent());
        var durable = new FoundgineProviderRequirement(FoundgineProviderKind.EVENT_STORE,
                FoundgineProviderCapability.mask(FoundgineProviderCapability.DURABLE_STORAGE), "must be durable");
        assertThrows(FoundgineProviderNegotiationException.class, () ->
                FoundgineProviderStartup.validateAndRegister(List.of(), List.of(durable), store, registry));
    }

    @Test void gatewayFacadeSubscribes() throws Exception {
        var gateway = new FoundgineGateway(new FoundgineGatewayOptions());
        assertNotNull(gateway.subscribe());
        gateway.subscribe().close();
    }

    @Test void optionsValidateBounds() {
        assertThrows(IllegalArgumentException.class, () -> new FoundgineGatewayOptions().replayPageSize(0));
        assertThrows(IllegalArgumentException.class, () -> new FoundgineGatewayOptions().replayPageSize(10_001));
        assertThrows(IllegalArgumentException.class, () -> new FoundgineGatewayOptions().subscriberBufferCapacity(0));
        assertThrows(IllegalArgumentException.class, () -> new FoundgineGatewayEventStreamOptions().heartbeatInterval(Duration.ZERO).validate());
    }
}
