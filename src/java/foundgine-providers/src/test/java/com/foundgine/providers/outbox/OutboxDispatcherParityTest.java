package com.foundgine.providers.outbox;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.providers.gateway.FoundgineGatewayEventType;
import com.foundgine.providers.gateway.FoundgineProviderCapability;
import com.foundgine.providers.json.FoundgineJson;
import java.lang.reflect.Proxy;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.*;
import org.junit.jupiter.api.Test;

class OutboxDispatcherParityTest {
    static final class FakeStore implements IFoundgineGatewayOutboxStore {
        final List<FoundgineOutboxLease> pending = new ArrayList<>();
        final List<String> log = new ArrayList<>();
        Instant lastRetryAt; String lastError;
        @Override public List<FoundgineOutboxLease> lease(int batch, Duration d, Instant now) {
            var out = new ArrayList<>(pending); pending.clear(); return out;
        }
        @Override public boolean complete(FoundgineOutboxLease l, Instant now) { log.add("complete"); return true; }
        @Override public boolean fail(FoundgineOutboxLease l, String error, Instant retryAt, Instant now) {
            log.add(retryAt == null ? "dead" : "retry"); lastRetryAt = retryAt; lastError = error; return true;
        }
    }

    private static FoundgineOutboxLease lease(int attempt) {
        return new FoundgineOutboxLease(new FoundgineOutboxMessage(UUID.randomUUID(), "t", "topic", "{}", null, "corr", null, null), attempt, UUID.randomUUID());
    }

    private static final Clock CLOCK = Clock.fixed(Instant.parse("2026-11-08T00:00:00Z"), ZoneOffset.UTC);

    @Test void backoffIsExponentialAndCapped() {
        var o = new FoundgineOutboxDispatcherOptions().baseBackoff(Duration.ofSeconds(2)).maxBackoff(Duration.ofSeconds(30));
        assertEquals(Duration.ofSeconds(2), FoundgineOutboxDispatcher.computeBackoff(1, o));
        assertEquals(Duration.ofSeconds(4), FoundgineOutboxDispatcher.computeBackoff(2, o));
        assertEquals(Duration.ofSeconds(16), FoundgineOutboxDispatcher.computeBackoff(4, o));
        assertEquals(Duration.ofSeconds(30), FoundgineOutboxDispatcher.computeBackoff(5, o));
        assertEquals(Duration.ofSeconds(30), FoundgineOutboxDispatcher.computeBackoff(1_000, o));
        assertEquals(Duration.ofSeconds(2), FoundgineOutboxDispatcher.computeBackoff(0, o));
    }

    @Test void successCompletesTheLease() {
        var store = new FakeStore(); store.pending.add(lease(1));
        var published = new ArrayList<String>();
        var d = new FoundgineOutboxDispatcher(store, m -> published.add(m.topic()), new FoundgineOutboxDispatcherOptions(), null, CLOCK);
        assertEquals(1, d.dispatchOnce());
        assertEquals(List.of("topic"), published);
        assertEquals(List.of("complete"), store.log);
        assertEquals(0, d.dispatchOnce());
    }

    @Test void failureSchedulesRetryWithBackoffThenDeadLettersAndAnnouncesWithoutPayload() {
        var store = new FakeStore();
        var announced = new ArrayList<Map<String, String>>();
        var subjects = new ArrayList<String>();
        var opts = new FoundgineOutboxDispatcherOptions().maxAttempts(2);
        var d = new FoundgineOutboxDispatcher(store, m -> { throw new IllegalStateException("broker down"); }, opts,
                (type, subject, corr, provider, attrs) -> {
                    assertEquals(FoundgineGatewayEventType.CUSTOM, type); subjects.add(subject); announced.add(attrs);
                }, CLOCK);
        store.pending.add(lease(1));
        d.dispatchOnce();
        assertEquals(List.of("retry"), store.log);
        assertEquals(CLOCK.instant().plus(Duration.ofSeconds(2)), store.lastRetryAt);
        assertEquals("IllegalStateException: broker down", store.lastError);
        assertTrue(announced.isEmpty());

        store.pending.add(lease(2));
        d.dispatchOnce();
        assertEquals("dead", store.log.get(1));
        assertNull(store.lastRetryAt);
        assertEquals(List.of("outbox.dead_lettered"), subjects);
        assertEquals(Set.of("messageId", "topic", "attempts"), announced.get(0).keySet());
        assertEquals("2", announced.get(0).get("attempts"));
    }

    @Test void interruptionLeavesLeasesToExpireInsteadOfSchedulingBrokerRetries() {
        var store = new FakeStore();
        store.pending.add(lease(1));
        store.pending.add(lease(1));
        var d = new FoundgineOutboxDispatcher(store, m -> { throw new InterruptedException("shutdown"); },
                new FoundgineOutboxDispatcherOptions(), null, CLOCK);
        try {
            assertEquals(0, d.dispatchOnce());
            assertTrue(Thread.currentThread().isInterrupted());
            assertTrue(store.log.isEmpty(), "interrupted delivery must not be completed, retried or dead-lettered");
        } finally {
            // Keep the JUnit worker thread reusable for subsequent tests.
            Thread.interrupted();
        }
    }

    @Test void deadLetterAnnouncementFailureIsSwallowed() {
        var store = new FakeStore(); store.pending.add(lease(8));
        var d = new FoundgineOutboxDispatcher(store, m -> { throw new Exception("x"); }, new FoundgineOutboxDispatcherOptions(),
                (t, s, c, p, a) -> { throw new RuntimeException("hub down"); }, CLOCK);
        assertEquals(1, d.dispatchOnce());
        assertEquals(List.of("dead"), store.log);
    }

    @Test void optionsValidateAndRequirementNamesRetryAndDeadLetter() {
        assertThrows(IllegalArgumentException.class, () -> new FoundgineOutboxDispatcherOptions().batchSize(0).validate());
        assertThrows(IllegalArgumentException.class, () -> new FoundgineOutboxDispatcherOptions().baseBackoff(Duration.ofMinutes(10)).validate());
        var req = FoundgineOutboxDispatcher.requirement();
        assertEquals(FoundgineProviderCapability.RETRY.bit() | FoundgineProviderCapability.DEAD_LETTER.bit(), req.required());
    }

    @Test void postgresOutboxValidatesBeforeTouchingTheDatabase() {
        var noDb = (javax.sql.DataSource) Proxy.newProxyInstance(getClass().getClassLoader(), new Class<?>[] {javax.sql.DataSource.class},
                (p, m, a) -> { throw new AssertionError("database must not be reached"); });
        var outbox = new PostgresOutbox(noDb, new PostgresOutboxOptions().maxPayloadBytes(10).maxHeaders(1), CLOCK);
        var id = UUID.randomUUID();
        assertThrows(IllegalArgumentException.class, () -> outbox.enqueue(new FoundgineOutboxMessage(new UUID(0, 0), "t", "topic", "{}")));
        assertThrows(IllegalArgumentException.class, () -> outbox.enqueue(new FoundgineOutboxMessage(id, " ", "topic", "{}")));
        assertThrows(IllegalArgumentException.class, () -> outbox.enqueue(new FoundgineOutboxMessage(id, "t", "topic", " ")));
        assertThrows(IllegalArgumentException.class, () -> outbox.enqueue(new FoundgineOutboxMessage(id, "t", "topic", "{\"too\":\"large\"}")));
        assertThrows(IllegalArgumentException.class, () -> outbox.enqueue(new FoundgineOutboxMessage(id, "t", "topic", "{}", null, null, Map.of("a", "1", "b", "2"), null)));
        assertThrows(IllegalArgumentException.class, () -> outbox.lease(0, Duration.ofSeconds(1), Instant.now()));
        assertThrows(IllegalArgumentException.class, () -> outbox.lease(1, Duration.ZERO, Instant.now()));
        assertThrows(IllegalArgumentException.class, () -> outbox.deleteCompleted(Instant.now(), 0));
        long caps = outbox.capabilities();
        assertEquals(FoundgineProviderCapability.TRANSACTIONAL_OUTBOX.bit(), caps & FoundgineProviderCapability.TRANSACTIONAL_OUTBOX.bit());
    }

    @Test void headerJsonRoundTripsEscapesAndRejectsNonStrings() {
        var headers = new LinkedHashMap<String, String>();
        headers.put("a\"b", "line\nbreak \u0001 é");
        headers.put("k", "v");
        assertEquals(headers, FoundgineJson.parseStringMap(FoundgineJson.stringMapToJson(headers)));
        assertEquals(Map.of(), FoundgineJson.parseStringMap("{}"));
        assertThrows(IllegalArgumentException.class, () -> FoundgineJson.parseStringMap("{\"a\":1}"));
        assertThrows(IllegalArgumentException.class, () -> FoundgineJson.parseStringMap("{\"a\":\"b\"} x"));
    }
}
