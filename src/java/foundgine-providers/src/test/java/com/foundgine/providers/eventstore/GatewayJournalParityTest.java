package com.foundgine.providers.eventstore;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.providers.eventstore.schema.EventStoreMigrator;
import com.foundgine.providers.gateway.FoundgineGatewayEvent;
import com.foundgine.providers.gateway.FoundgineGatewayEventJson;
import com.foundgine.providers.gateway.FoundgineGatewayEventType;
import java.lang.reflect.Proxy;
import java.time.Instant;
import java.util.*;
import javax.sql.DataSource;
import org.junit.jupiter.api.Test;

class GatewayJournalParityTest {
    /** A DataSource that fails the test if validation lets a call reach the database. */
    static final DataSource NO_DB = (DataSource) Proxy.newProxyInstance(GatewayJournalParityTest.class.getClassLoader(),
            new Class<?>[] {DataSource.class}, (p, m, a) -> { throw new AssertionError("database must not be reached: " + m.getName()); });

    /** In-memory IEventStore with the Postgres contract (idempotent by event id, gap-free sequence, bounded reads). */
    static final class MemoryEventStore implements IEventStore {
        record Row(UUID id, String tenant, String stream, long seq, String type, Instant at, String json, String corr) { }
        final List<Row> rows = new ArrayList<>();
        final Map<String, Long> last = new HashMap<>();
        int reads;

        @Override public synchronized <T> long append(String t, String s, UUID id, String type, Instant at, T p,
                PayloadCodec<T> c, String corr) {
            for (var r : rows) if (r.tenant.equals(t) && r.id.equals(id)) return r.seq;
            long seq = last.merge(t + "|" + s, 1L, Long::sum);
            rows.add(new Row(id, t, s, seq, type, at, c.toJson(p), corr));
            return seq;
        }
        @Override public synchronized <T> List<EventEnvelope<T>> read(String t, String s, long after, Integer take, PayloadCodec<T> c) {
            reads++;
            int limit = take == null ? 100 : take;
            return rows.stream().filter(r -> r.tenant.equals(t) && r.stream.equals(s) && r.seq > after).limit(limit)
                    .map(r -> new EventEnvelope<>(r.id, r.tenant, r.stream, r.seq, r.type, r.at, c.fromJson(r.json), r.corr)).toList();
        }
        @Override public synchronized <T> List<EventEnvelope<T>> readByCorrelation(String t, String s, String corr, int take, PayloadCodec<T> c) {
            var all = rows.stream().filter(r -> r.tenant.equals(t) && r.stream.equals(s) && corr.equals(r.corr)).toList();
            return all.subList(Math.max(0, all.size() - take), all.size()).stream()
                    .map(r -> new EventEnvelope<>(r.id, r.tenant, r.stream, r.seq, r.type, r.at, c.fromJson(r.json), r.corr)).toList();
        }
        @Override public synchronized long getLatestSequence(String t, String s) { return last.getOrDefault(t + "|" + s, 0L); }
        @Override public int deleteExpired(Instant olderThan, int batchSize) { return 0; }
    }

    private static FoundgineGatewayEvent event(String subject, String corr) {
        return FoundgineGatewayEvent.create(FoundgineGatewayEventType.CUSTOM, subject, corr, "p1", Map.of("k", "v \"q\"\n"));
    }

    @Test void eventJsonMatchesDotNetShapeAndRoundTrips() {
        var id = UUID.fromString("11111111-2222-3333-4444-555555555555");
        var e = new FoundgineGatewayEvent(id, 7, Instant.parse("2026-11-08T10:15:30Z"),
                FoundgineGatewayEventType.PROVIDER_HEALTH_CHANGED, "provider.health", null, "pg", Map.of("a", "b"));
        String json = FoundgineGatewayEventJson.toJson(e);
        assertEquals("{\"sequence\":7,\"occurredAt\":\"2026-11-08T10:15:30Z\",\"type\":\"ProviderHealthChanged\","
                + "\"subject\":\"provider.health\",\"correlationId\":null,\"providerId\":\"pg\","
                + "\"attributes\":{\"a\":\"b\"},\"eventId\":\"11111111-2222-3333-4444-555555555555\"}", json);
        assertEquals(e, FoundgineGatewayEventJson.fromJson(json));
    }

    @Test void eventJsonAcceptsDotNetOffsetTimestampsAndRejectsUnmappedMembers() {
        String dotnet = "{\"sequence\":3,\"occurredAt\":\"2026-11-08T10:15:30.1234567+00:00\",\"type\":\"Custom\","
                + "\"subject\":\"s\",\"correlationId\":\"c\",\"providerId\":null,\"attributes\":{},"
                + "\"eventId\":\"11111111-2222-3333-4444-555555555555\"}";
        var e = FoundgineGatewayEventJson.fromJson(dotnet);
        assertEquals(3L, e.sequence());
        assertEquals(FoundgineGatewayEventType.CUSTOM, e.type());
        assertEquals("c", e.correlationId());
        assertThrows(IllegalArgumentException.class, () -> FoundgineGatewayEventJson.fromJson(dotnet.replace("\"subject\"", "\"extra\":1,\"subject\"")));
        assertThrows(IllegalArgumentException.class, () -> FoundgineGatewayEventJson.fromJson(dotnet.replace("Custom", "Nope")));
    }

    @Test void everyEventTypeRoundTripsThroughItsWireName() {
        for (var t : FoundgineGatewayEventType.values())
            assertEquals(t, FoundgineGatewayEventJson.parseType(FoundgineGatewayEventJson.typeName(t)));
        assertEquals("AuthorizationDenied", FoundgineGatewayEventJson.typeName(FoundgineGatewayEventType.AUTHORIZATION_DENIED));
    }

    @Test void journalAppendAssignsGapFreeSequencesAndIsIdempotentByEventId() {
        var journal = new PostgresGatewayEventStore(new MemoryEventStore(), new PostgresGatewayEventStoreOptions());
        var a = journal.append(event("a", "c1"));
        var b = journal.append(event("b", "c1"));
        assertEquals(1L, a.sequence());
        assertEquals(2L, b.sequence());
        assertEquals(1L, journal.append(a).sequence());
        assertEquals(2L, journal.getLatestSequence());
    }

    @Test void journalReadAfterPagesThroughTheStore() {
        var store = new MemoryEventStore();
        var journal = new PostgresGatewayEventStore(store, new PostgresGatewayEventStoreOptions().pageSize(2));
        for (int i = 0; i < 5; i++) journal.append(event("e" + i, null));
        var all = journal.readAfter(0, 10);
        assertEquals(5, all.size());
        assertEquals(List.of(1L, 2L, 3L, 4L, 5L), all.stream().map(FoundgineGatewayEvent::sequence).toList());
        assertEquals(3, store.reads);
        assertEquals(2, journal.readAfter(2, 2).size());
        assertEquals("e2", journal.readAfter(2, 1).get(0).subject());
    }

    @Test void journalReadByCorrelationReturnsLatestMatchesOldestFirst() {
        var journal = new PostgresGatewayEventStore(new MemoryEventStore(), new PostgresGatewayEventStoreOptions());
        for (int i = 0; i < 4; i++) journal.append(event("m" + i, "corr"));
        journal.append(event("other", "x"));
        var latest = journal.readByCorrelationId("corr", 2);
        assertEquals(List.of("m2", "m3"), latest.stream().map(FoundgineGatewayEvent::subject).toList());
        assertThrows(IllegalArgumentException.class, () -> journal.readByCorrelationId(" ", 1));
        assertThrows(IllegalArgumentException.class, () -> journal.readAfter(-1, 1));
        assertThrows(IllegalArgumentException.class, () -> journal.readAfter(0, 10_001));
    }

    @Test void journalDoesNotDeclareTenantIsolationButPostgresStoreDoes() {
        long journal = new PostgresGatewayEventStore(new MemoryEventStore(), new PostgresGatewayEventStoreOptions()).capabilities();
        long store = new PostgresEventStore(NO_DB).capabilities();
        long tenant = com.foundgine.providers.gateway.FoundgineProviderCapability.TENANT_ISOLATION.bit();
        assertEquals(0L, journal & tenant);
        assertEquals(tenant, store & tenant);
    }

    @Test void postgresEventStoreValidatesBeforeTouchingTheDatabase() {
        var store = new PostgresEventStore(NO_DB);
        PayloadCodec<String> codec = new PayloadCodec<>() {
            public String toJson(String p) { return "\"" + p + "\""; }
            public String fromJson(String j) { return j; }
        };
        var id = UUID.randomUUID();
        assertThrows(IllegalArgumentException.class, () -> store.append(" ", "s", id, "t", Instant.now(), "p", codec, null));
        assertThrows(IllegalArgumentException.class, () -> store.append("t", "s".repeat(201), id, "t", Instant.now(), "p", codec, null));
        assertThrows(IllegalArgumentException.class, () -> store.append("t", "s", new UUID(0, 0), "t", Instant.now(), "p", codec, null));
        assertThrows(IllegalArgumentException.class, () -> store.read("t", "s", -1, null, codec));
        assertThrows(IllegalArgumentException.class, () -> store.read("t", "s", 0, 501, codec));
        assertThrows(IllegalArgumentException.class, () -> store.deleteExpired(Instant.now(), 0));
        assertThrows(IllegalArgumentException.class, () -> new EventStoreOptions().defaultReplayBatchSize(501).validate());
    }

    @Test void migrationsShipAsResourcesInOrder() {
        assertEquals(List.of("001_initial", "002_correlation", "003_outbox", "004_gateway_approvals"), List.of(EventStoreMigrator.migrations()));
        for (String v : EventStoreMigrator.migrations()) assertTrue(EventStoreMigrator.load(v).contains("CREATE") || EventStoreMigrator.load(v).contains("ALTER"));
        assertTrue(EventStoreMigrator.load("003_outbox").contains("foundgine_outbox"));
        assertTrue(EventStoreMigrator.load("004_gateway_approvals").contains("foundgine_gateway_approvals"));
        assertThrows(IllegalStateException.class, () -> EventStoreMigrator.load("999_missing"));
    }
}
