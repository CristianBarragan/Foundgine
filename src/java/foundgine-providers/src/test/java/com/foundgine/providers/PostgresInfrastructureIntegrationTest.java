package com.foundgine.providers;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.providers.eventstore.PayloadCodec;
import com.foundgine.providers.eventstore.PostgresEventStore;
import com.foundgine.providers.eventstore.schema.EventStoreMigrator;
import com.foundgine.providers.outbox.FoundgineOutboxLease;
import com.foundgine.providers.outbox.FoundgineOutboxMessage;
import com.foundgine.providers.outbox.PostgresOutbox;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.time.Duration;
import java.time.Instant;
import java.time.temporal.ChronoUnit;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.Callable;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import javax.sql.DataSource;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.TestInstance;
import org.postgresql.ds.PGSimpleDataSource;

/**
 * Real-PostgreSQL integration tests for the migrator, event store and outbox.
 *
 * <p>Opt-in: set {@code FOUNDGINE_TEST_PG_URL} (a {@code jdbc:postgresql://...} URL, or the
 * semicolon form used by the other Postgres tests). {@code FOUNDGINE_POSTGRES_CONNECTION_STRING} is
 * accepted as a fallback. Without either, every test is skipped, so the build needs no Docker and no
 * database. Each run creates and drops its own schema; nothing outside it is touched.
 */
@TestInstance(TestInstance.Lifecycle.PER_CLASS)
class PostgresInfrastructureIntegrationTest {
    private static final ObjectMapper JSON = new ObjectMapper();
    private static final PayloadCodec<String> CODEC =
            new PayloadCodec<>() {
                @Override
                public String toJson(String payload) {
                    try {
                        return JSON.writeValueAsString(Map.of("v", payload));
                    } catch (Exception e) {
                        throw new IllegalStateException(e);
                    }
                }

                @Override
                public String fromJson(String json) {
                    try {
                        return JSON.readTree(json).get("v").asText();
                    } catch (Exception e) {
                        throw new IllegalStateException(e);
                    }
                }
            };

    private String baseUrl;
    private String schema;
    private DataSource dataSource;

    @BeforeAll
    void setUp() throws Exception {
        baseUrl = resolveUrl();
        Assumptions.assumeTrue(
                baseUrl != null, "Set FOUNDGINE_TEST_PG_URL to run the Postgres integration tests.");
        schema = "fg_it_" + UUID.randomUUID().toString().replace("-", "");
        try (Connection c = DriverManager.getConnection(baseUrl);
                Statement s = c.createStatement()) {
            s.execute("CREATE SCHEMA \"" + schema + "\"");
        }
        var ds = new PGSimpleDataSource();
        ds.setUrl(baseUrl);
        ds.setCurrentSchema(schema);
        dataSource = ds;
        EventStoreMigrator.apply(dataSource);
    }

    @AfterAll
    void tearDown() {
        if (baseUrl == null || schema == null) return;
        try (Connection c = DriverManager.getConnection(baseUrl);
                Statement s = c.createStatement()) {
            s.execute("DROP SCHEMA IF EXISTS \"" + schema + "\" CASCADE");
        } catch (SQLException ignored) {
            // best-effort cleanup
        }
    }

    // ---------------------------------------------------------------- migrator

    @Test
    void migrator_recordsEveryMigrationAndIsIdempotent() throws Exception {
        EventStoreMigrator.apply(dataSource);
        EventStoreMigrator.apply(dataSource);

        Set<String> applied = new HashSet<>();
        try (Connection c = dataSource.getConnection();
                Statement s = c.createStatement();
                ResultSet rs = s.executeQuery("SELECT version FROM foundgine_schema_migrations")) {
            while (rs.next()) applied.add(rs.getString(1));
        }
        assertEquals(Set.of(EventStoreMigrator.migrations()), applied);
        assertEquals(1L, count("foundgine_schema_migrations", "version = '001_initial'"));
    }

    @Test
    void migrator_concurrentHostsDoNotRaceTheMigrations() throws Exception {
        String raceSchema = "fg_it_race_" + UUID.randomUUID().toString().replace("-", "");
        try (Connection c = DriverManager.getConnection(baseUrl);
                Statement s = c.createStatement()) {
            s.execute("CREATE SCHEMA \"" + raceSchema + "\"");
        }
        try {
            var ds = new PGSimpleDataSource();
            ds.setUrl(baseUrl);
            ds.setCurrentSchema(raceSchema);
            runConcurrently(6, i -> {
                EventStoreMigrator.apply(ds);
                return null;
            });
            try (Connection c = ds.getConnection();
                    Statement s = c.createStatement();
                    ResultSet rs = s.executeQuery("SELECT count(*) FROM foundgine_schema_migrations")) {
                rs.next();
                assertEquals(EventStoreMigrator.migrations().length, rs.getInt(1));
            }
        } finally {
            try (Connection c = DriverManager.getConnection(baseUrl);
                    Statement s = c.createStatement()) {
                s.execute("DROP SCHEMA IF EXISTS \"" + raceSchema + "\" CASCADE");
            }
        }
    }

    // ---------------------------------------------------------------- event store

    @Test
    void eventStore_appendsSequentiallyAndRoundTripsPayloadTimeAndCorrelation() {
        var store = new PostgresEventStore(dataSource);
        String tenant = tenant();
        Instant at = Instant.parse("2026-03-04T05:06:07.123456Z");

        long first = store.append(tenant, "s1", UUID.randomUUID(), "t.one", at, "alpha \"q\" é", CODEC, "corr-1");
        long second = store.append(tenant, "s1", UUID.randomUUID(), "t.two", at, "beta", CODEC, null);

        assertEquals(1, first);
        assertEquals(2, second);
        assertEquals(2, store.getLatestSequence(tenant, "s1"));
        assertEquals(0, store.getLatestSequence(tenant, "missing"));

        var events = store.read(tenant, "s1", 0, null, CODEC);
        assertEquals(2, events.size());
        assertEquals("alpha \"q\" é", events.get(0).payload());
        assertEquals("corr-1", events.get(0).correlationId());
        assertNull(events.get(1).correlationId());
        assertEquals("t.one", events.get(0).eventType());
        // timestamptz keeps microseconds; the Instant must survive the OffsetDateTime binding unchanged.
        assertEquals(at, events.get(0).occurredAt());
    }

    @Test
    void eventStore_appendIsIdempotentByTenantAndEventId() {
        var store = new PostgresEventStore(dataSource);
        String tenant = tenant();
        UUID id = UUID.randomUUID();

        long a = store.append(tenant, "s", id, "t", Instant.now(), "x", CODEC, null);
        long b = store.append(tenant, "s", id, "t", Instant.now(), "x", CODEC, null);

        assertEquals(a, b);
        assertEquals(1, store.read(tenant, "s", 0, null, CODEC).size());
        assertEquals(1, store.getLatestSequence(tenant, "s"));
    }

    @Test
    void eventStore_readHonoursAfterSequenceTakeAndTenantIsolation() {
        var store = new PostgresEventStore(dataSource);
        String tenantA = tenant();
        String tenantB = tenant();
        for (int i = 1; i <= 5; i++)
            store.append(tenantA, "s", UUID.randomUUID(), "t", Instant.now(), "e" + i, CODEC, null);
        store.append(tenantB, "s", UUID.randomUUID(), "t", Instant.now(), "other", CODEC, null);

        var page = store.read(tenantA, "s", 2, 2, CODEC);
        assertEquals(List.of(3L, 4L), page.stream().map(e -> e.sequence()).toList());
        assertEquals(1, store.read(tenantB, "s", 0, null, CODEC).size());
        assertEquals(5, store.getLatestSequence(tenantA, "s"));
        assertEquals(1, store.getLatestSequence(tenantB, "s"));
    }

    @Test
    void eventStore_readByCorrelationReturnsLatestMatchesOldestFirst() {
        var store = new PostgresEventStore(dataSource);
        String tenant = tenant();
        for (int i = 1; i <= 4; i++)
            store.append(tenant, "s", UUID.randomUUID(), "t", Instant.now(), "m" + i, CODEC, "c");
        store.append(tenant, "s", UUID.randomUUID(), "t", Instant.now(), "n", CODEC, "other");

        var latestTwo = store.readByCorrelation(tenant, "s", "c", 2, CODEC);

        assertEquals(List.of("m3", "m4"), latestTwo.stream().map(e -> e.payload()).toList());
    }

    @Test
    void eventStore_retentionDeletesOldEventsButNeverLowersTheHighWaterMark() {
        var store = new PostgresEventStore(dataSource);
        String tenant = tenant();
        Instant old = Instant.now().minus(30, ChronoUnit.DAYS);
        store.append(tenant, "s", UUID.randomUUID(), "t", old, "old", CODEC, null);
        store.append(tenant, "s", UUID.randomUUID(), "t", Instant.now(), "new", CODEC, null);

        int deleted = store.deleteExpired(Instant.now().minus(1, ChronoUnit.DAYS));

        assertTrue(deleted >= 1);
        var left = store.read(tenant, "s", 0, null, CODEC);
        assertEquals(List.of("new"), left.stream().map(e -> e.payload()).toList());
        assertEquals(2, store.getLatestSequence(tenant, "s"));
        long next = store.append(tenant, "s", UUID.randomUUID(), "t", Instant.now(), "after", CODEC, null);
        assertEquals(3, next);
    }

    @Test
    void eventStore_concurrentAppendsToOneStreamAllocateUniqueContiguousSequences() throws Exception {
        var store = new PostgresEventStore(dataSource);
        String tenant = tenant();
        int threads = 8;
        int perThread = 10;

        List<List<Long>> results =
                runConcurrently(
                        threads,
                        t -> {
                            List<Long> mine = new ArrayList<>();
                            for (int i = 0; i < perThread; i++)
                                mine.add(
                                        store.append(
                                                tenant,
                                                "hot",
                                                UUID.randomUUID(),
                                                "t",
                                                Instant.now(),
                                                "t" + t + "-" + i,
                                                CODEC,
                                                null));
                            return mine;
                        });

        Set<Long> all = new HashSet<>();
        results.forEach(all::addAll);
        assertEquals(threads * perThread, all.size());
        for (long s = 1; s <= threads * perThread; s++) assertTrue(all.contains(s), "missing " + s);
        assertEquals(threads * perThread, store.getLatestSequence(tenant, "hot"));
    }

    @Test
    void eventStore_concurrentDuplicateEventIdStoresExactlyOneEvent() throws Exception {
        var store = new PostgresEventStore(dataSource);
        String tenant = tenant();
        UUID id = UUID.randomUUID();

        List<Long> sequences =
                runConcurrently(
                        8, i -> store.append(tenant, "dup", id, "t", Instant.now(), "same", CODEC, null));

        assertEquals(Set.of(1L), new HashSet<>(sequences));
        assertEquals(1, store.read(tenant, "dup", 0, null, CODEC).size());
    }

    // ---------------------------------------------------------------- outbox

    @Test
    void outbox_enqueueLeaseCompleteRoundTripsHeadersAndPayload() {
        var outbox = new PostgresOutbox(dataSource);
        String tenant = tenant();
        UUID id = UUID.randomUUID();
        Instant created = Instant.parse("2026-01-02T03:04:05.250Z");
        outbox.enqueue(
                new FoundgineOutboxMessage(
                        id,
                        tenant,
                        "orders",
                        "{\"n\":1}",
                        "key-1",
                        "corr-9",
                        Map.of("h1", "v1", "h2", "ü"),
                        created));

        Instant now = Instant.now();
        var leases = outbox.lease(10, Duration.ofSeconds(30), now);
        var mine = leases.stream().filter(l -> l.message().tenantId().equals(tenant)).toList();

        assertEquals(1, mine.size());
        var lease = mine.get(0);
        assertEquals(1, lease.attempt());
        assertEquals(id, lease.message().id());
        assertEquals("orders", lease.message().topic());
        assertEquals("key-1", lease.message().key());
        assertEquals("corr-9", lease.message().correlationId());
        assertEquals(Map.of("h1", "v1", "h2", "ü"), lease.message().headers());
        assertEquals(created, lease.message().createdAt());
        assertEquals(1, JSON_N(lease.message().payloadJson()));

        assertTrue(outbox.complete(lease, now));
        assertFalse(outbox.complete(lease, now), "second completion must report the message is finished");
        assertTrue(leaseFor(outbox, tenant, now.plusSeconds(120)).isEmpty());
    }

    @Test
    void outbox_enqueueIsIdempotentByTenantAndId() {
        var outbox = new PostgresOutbox(dataSource);
        String tenant = tenant();
        UUID id = UUID.randomUUID();
        outbox.enqueue(new FoundgineOutboxMessage(id, tenant, "t", "{\"a\":1}"));
        outbox.enqueue(new FoundgineOutboxMessage(id, tenant, "t", "{\"a\":2}"));

        assertEquals(1L, count("foundgine_outbox", "tenant_id = '" + tenant + "'"));
    }

    @Test
    void outbox_leasedMessageIsExclusiveUntilTheLeaseExpiresAndStaleHoldersLose() {
        var outbox = new PostgresOutbox(dataSource);
        String tenant = tenant();
        outbox.enqueue(new FoundgineOutboxMessage(UUID.randomUUID(), tenant, "t", "{\"a\":1}"));
        Instant now = Instant.now();

        var first = leaseFor(outbox, tenant, now);
        assertEquals(1, first.size());
        assertTrue(leaseFor(outbox, tenant, now.plusSeconds(5)).isEmpty(), "still leased");

        var second = leaseFor(outbox, tenant, now.plusSeconds(120));
        assertEquals(1, second.size());
        assertEquals(2, second.get(0).attempt());

        assertFalse(outbox.complete(first.get(0), now.plusSeconds(121)), "stale holder must lose");
        assertTrue(outbox.complete(second.get(0), now.plusSeconds(121)));
    }

    @Test
    void outbox_failSchedulesRetryThenDeadLetters() {
        var outbox = new PostgresOutbox(dataSource);
        String tenant = tenant();
        outbox.enqueue(new FoundgineOutboxMessage(UUID.randomUUID(), tenant, "t", "{\"a\":1}"));
        Instant now = Instant.now();

        var lease = leaseFor(outbox, tenant, now).get(0);
        assertTrue(outbox.fail(lease, "boom", now.plusSeconds(60), now));
        assertTrue(leaseFor(outbox, tenant, now.plusSeconds(30)).isEmpty(), "not due before retryAt");

        var retry = leaseFor(outbox, tenant, now.plusSeconds(61)).get(0);
        assertEquals(2, retry.attempt());
        assertTrue(outbox.fail(retry, "x".repeat(5000), null, now.plusSeconds(62)));

        assertTrue(leaseFor(outbox, tenant, now.plusSeconds(10_000)).isEmpty(), "dead-lettered");
        assertEquals(1L, count("foundgine_outbox", "tenant_id = '" + tenant + "' AND dead_lettered_at IS NOT NULL"));
        assertEquals(
                2000L,
                scalar("SELECT length(last_error) FROM foundgine_outbox WHERE tenant_id = '" + tenant + "'"));
    }

    @Test
    void outbox_enqueueOnCallerConnectionIsAtomicWithTheCallersTransaction() throws Exception {
        var outbox = new PostgresOutbox(dataSource);
        String tenant = tenant();

        try (Connection c = dataSource.getConnection()) {
            c.setAutoCommit(false);
            outbox.enqueue(c, new FoundgineOutboxMessage(UUID.randomUUID(), tenant, "t", "{\"a\":1}"));
            assertEquals(0L, count("foundgine_outbox", "tenant_id = '" + tenant + "'"), "invisible before commit");
            c.rollback();
        }
        assertEquals(0L, count("foundgine_outbox", "tenant_id = '" + tenant + "'"), "rolled back");

        try (Connection c = dataSource.getConnection()) {
            c.setAutoCommit(false);
            outbox.enqueue(c, new FoundgineOutboxMessage(UUID.randomUUID(), tenant, "t", "{\"a\":1}"));
            c.commit();
        }
        assertEquals(1L, count("foundgine_outbox", "tenant_id = '" + tenant + "'"));
    }

    @Test
    void outbox_concurrentDispatchersLeaseDisjointMessages() throws Exception {
        var outbox = new PostgresOutbox(dataSource);
        String tenant = tenant();
        int messages = 40;
        for (int i = 0; i < messages; i++)
            outbox.enqueue(new FoundgineOutboxMessage(UUID.randomUUID(), tenant, "t", "{\"i\":" + i + "}"));
        Instant now = Instant.now();

        List<List<FoundgineOutboxLease>> batches =
                runConcurrently(6, i -> leaseFor(outbox, tenant, now, 7));

        List<UUID> ids = new ArrayList<>();
        batches.forEach(b -> b.forEach(l -> ids.add(l.message().id())));
        assertEquals(new HashSet<>(ids).size(), ids.size(), "a message was leased to two dispatchers");
        assertTrue(ids.size() <= messages);
        assertTrue(ids.size() >= 7, "at least one dispatcher must have leased a full batch");
    }

    @Test
    void outbox_retentionDeletesOnlyOldCompletedRowsAndKeepsDeadLetters() {
        var outbox = new PostgresOutbox(dataSource);
        String tenant = tenant();
        outbox.enqueue(new FoundgineOutboxMessage(UUID.randomUUID(), tenant, "done", "{\"a\":1}"));
        outbox.enqueue(new FoundgineOutboxMessage(UUID.randomUUID(), tenant, "dead", "{\"a\":1}"));
        Instant now = Instant.now();
        var leases = leaseFor(outbox, tenant, now);
        assertEquals(2, leases.size());
        for (var l : leases) {
            if (l.message().topic().equals("done")) assertTrue(outbox.complete(l, now.minus(10, ChronoUnit.DAYS)));
            else assertTrue(outbox.fail(l, "dead", null, now));
        }

        int deleted = outbox.deleteCompleted(now.minus(1, ChronoUnit.DAYS));

        assertTrue(deleted >= 1);
        assertEquals(1L, count("foundgine_outbox", "tenant_id = '" + tenant + "'"));
        assertEquals(1L, count("foundgine_outbox", "tenant_id = '" + tenant + "' AND dead_lettered_at IS NOT NULL"));
    }

    // ---------------------------------------------------------------- helpers

    private static int JSON_N(String json) {
        try {
            return JSON.readTree(json).get("n").asInt();
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }

    private static String tenant() {
        return "t_" + UUID.randomUUID().toString().replace("-", "");
    }

    private List<FoundgineOutboxLease> leaseFor(PostgresOutbox outbox, String tenant, Instant now) {
        return leaseFor(outbox, tenant, now, 100);
    }

    /** Leases and keeps only this test's tenant, so tests sharing the schema cannot see each other. */
    private List<FoundgineOutboxLease> leaseFor(PostgresOutbox outbox, String tenant, Instant now, int batch) {
        return outbox.lease(batch, Duration.ofSeconds(30), now).stream()
                .filter(l -> l.message().tenantId().equals(tenant))
                .toList();
    }

    private long count(String table, String where) {
        return scalar("SELECT count(*) FROM " + table + " WHERE " + where);
    }

    private long scalar(String sql) {
        try (Connection c = dataSource.getConnection();
                PreparedStatement ps = c.prepareStatement(sql);
                ResultSet rs = ps.executeQuery()) {
            rs.next();
            return rs.getLong(1);
        } catch (SQLException e) {
            throw new IllegalStateException(e);
        }
    }

    private static <T> List<T> runConcurrently(int threads, ThrowingFunction<T> task) throws Exception {
        ExecutorService pool = Executors.newFixedThreadPool(threads);
        try {
            CountDownLatch start = new CountDownLatch(1);
            List<Future<T>> futures = new ArrayList<>();
            for (int i = 0; i < threads; i++) {
                final int index = i;
                futures.add(
                        pool.submit(
                                (Callable<T>)
                                        () -> {
                                            start.await();
                                            return task.apply(index);
                                        }));
            }
            start.countDown();
            List<T> results = new ArrayList<>();
            for (Future<T> f : futures) results.add(f.get());
            return results;
        } finally {
            pool.shutdownNow();
        }
    }

    @FunctionalInterface
    private interface ThrowingFunction<T> {
        T apply(int index) throws Exception;
    }

    private static String resolveUrl() {
        String value = System.getenv("FOUNDGINE_TEST_PG_URL");
        if (value == null || value.isBlank()) value = System.getenv("FOUNDGINE_POSTGRES_CONNECTION_STRING");
        if (value == null || value.isBlank()) return null;
        if (value.startsWith("jdbc:")) return value;
        Map<String, String> parts = new java.util.HashMap<>();
        for (String item : value.split(";")) {
            int eq = item.indexOf('=');
            if (eq > 0)
                parts.put(
                        item.substring(0, eq).trim().toLowerCase(java.util.Locale.ROOT),
                        item.substring(eq + 1).trim());
        }
        return "jdbc:postgresql://"
                + parts.getOrDefault("host", "localhost")
                + ":"
                + parts.getOrDefault("port", "5432")
                + "/"
                + parts.getOrDefault("database", "postgres")
                + "?user="
                + parts.getOrDefault("username", parts.getOrDefault("user", "postgres"))
                + "&password="
                + parts.getOrDefault("password", "");
    }
}
