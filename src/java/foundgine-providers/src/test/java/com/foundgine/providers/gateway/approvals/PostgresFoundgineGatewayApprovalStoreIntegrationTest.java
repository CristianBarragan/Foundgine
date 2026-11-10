package com.foundgine.providers.gateway.approvals;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.semantic.mutation.SemanticMutationKind;
import com.foundgine.core.semantic.mutation.SemanticMutationOperation;
import com.foundgine.core.semantic.mutation.SemanticMutationOperationGraph;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.core.semantic.security.warrants.SecurityWarrant;
import com.foundgine.core.semantic.security.warrants.SecurityWarrantConstraints;
import com.foundgine.providers.eventstore.schema.EventStoreMigrator;
import com.foundgine.providers.outbox.FoundgineOutboxLease;
import com.foundgine.providers.outbox.PostgresOutbox;
import com.foundgine.runtime.MutationPlanApproval;
import com.foundgine.runtime.SemanticMutationRequest;
import java.security.MessageDigest;
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
import java.util.HexFormat;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.Callable;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.atomic.AtomicBoolean;
import javax.sql.DataSource;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.TestInstance;
import org.postgresql.ds.PGSimpleDataSource;

/**
 * Real-PostgreSQL integration tests for {@link PostgresFoundgineGatewayApprovalStore}.
 *
 * <p>Opt-in with the same convention as {@code PostgresInfrastructureIntegrationTest}: set
 * {@code FOUNDGINE_TEST_PG_URL} (a {@code jdbc:postgresql://...} URL or the semicolon connection-string form);
 * {@code FOUNDGINE_POSTGRES_CONNECTION_STRING} is accepted as a fallback. Without either, every test is skipped.
 * The suite creates its own schema and drops it afterwards. Scenario names mirror the .NET suite
 * {@code PostgresGatewayApprovalStoreIntegrationTests}; a conformance test enforces that parity.
 */
@TestInstance(TestInstance.Lifecycle.PER_CLASS)
class PostgresFoundgineGatewayApprovalStoreIntegrationTest {
    private static final ObjectMapper JSON = new ObjectMapper();
    private static final Instant NOW = Instant.parse("2026-01-01T00:00:00Z");

    private String baseUrl;
    private String schema;
    private DataSource dataSource;
    private TestCodec codec;
    private PostgresFoundgineGatewayApprovalStore store;

    @BeforeAll
    void setUp() throws Exception {
        baseUrl = resolveUrl();
        Assumptions.assumeTrue(baseUrl != null, "Set FOUNDGINE_TEST_PG_URL to run the Postgres integration tests.");
        schema = "fg_ap_" + UUID.randomUUID().toString().replace("-", "");
        try (Connection c = DriverManager.getConnection(baseUrl); Statement s = c.createStatement()) {
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
        try (Connection c = DriverManager.getConnection(baseUrl); Statement s = c.createStatement()) {
            s.execute("DROP SCHEMA IF EXISTS \"" + schema + "\" CASCADE");
        } catch (SQLException ignored) {
            // best-effort cleanup
        }
    }

    @BeforeEach
    void resetTables() throws Exception {
        try (Connection c = dataSource.getConnection(); Statement s = c.createStatement()) {
            s.execute("TRUNCATE foundgine_gateway_approvals, foundgine_outbox");
        }
        codec = new TestCodec();
        // Most tests use a deterministic historical clock; database-clock enforcement is tested separately.
        store = new PostgresFoundgineGatewayApprovalStore(dataSource, codec, false);
    }

    // ------------------------------------------------------------ atomic commit / rollback

    @Test
    void storeWithEvidence_commitsApprovalAndOutboxTogether() throws Exception {
        var digest = digest("commit");
        var evidence = evidence("alice", "tenant-a", NOW, NOW.plus(10, ChronoUnit.MINUTES));
        store.storeWithEvidence(digest, approval("alice", "tenant-a"), evidence);

        assertEquals(1L, count("foundgine_gateway_approvals", "handle_digest = '" + digest + "'"));
        assertEquals(1L, count("foundgine_outbox", "tenant_id = 'tenant-a' AND topic = 'ApprovalRequested'"));
        try (Connection c = dataSource.getConnection(); Statement s = c.createStatement();
                ResultSet rs = s.executeQuery("SELECT message_key, payload::text FROM foundgine_outbox")) {
            assertTrue(rs.next());
            assertEquals(UUID.fromString(dashed(evidence.eventId())).toString(), rs.getString(1),
                    "message key is the event id");
            assertNotEquals(digest, rs.getString(1), "the handle digest must never reach the outbox key");
            assertFalse(rs.getString(2).contains(digest), "the handle digest must never reach the outbox payload");
        }
    }

    @Test
    void outboxFailure_rollsBackApproval() throws Exception {
        var evidence = evidence("alice", "tenant-a", NOW, NOW.plus(10, ChronoUnit.MINUTES));
        // Pre-seed an outbox row with the same (tenant_id, id) so the second insert violates the primary key.
        try (Connection c = dataSource.getConnection(); PreparedStatement p = c.prepareStatement(
                "INSERT INTO foundgine_outbox(tenant_id,id,topic,payload,created_at,available_at) "
                        + "VALUES ('tenant-a',?::uuid,'x','{}'::jsonb,now(),now())")) {
            p.setString(1, dashed(evidence.eventId()));
            p.executeUpdate();
        }
        var digest = digest("outbox-fail");
        assertThrows(IllegalStateException.class,
                () -> store.storeWithEvidence(digest, approval("alice", "tenant-a"), evidence));

        assertEquals(0L, count("foundgine_gateway_approvals", "TRUE"), "approval must roll back with the outbox failure");
        assertEquals(1L, count("foundgine_outbox", "TRUE"), "only the pre-seeded row remains");
        assertTrue(store.take(digest, "alice", "tenant-a", NOW).isEmpty());
    }

    @Test
    void approvalFailure_leavesNoOutboxRowAndKeepsOriginalApproval() throws Exception {
        var digest = digest("dup");
        store.storeWithEvidence(digest, approval("alice", "tenant-a"),
                evidence("alice", "tenant-a", NOW, NOW.plus(10, ChronoUnit.MINUTES)));
        assertThrows(IllegalStateException.class, () -> store.storeWithEvidence(digest, approval("mallory", "tenant-a"),
                evidence("mallory", "tenant-a", NOW, NOW.plus(10, ChronoUnit.MINUTES))));

        assertEquals(1L, count("foundgine_outbox", "TRUE"), "duplicate digest must not add an outbox row");
        assertTrue(store.take(digest, "mallory", "tenant-a", NOW).isEmpty(), "original owner keeps the approval");
        assertTrue(store.take(digest, "alice", "tenant-a", NOW).isPresent());
    }

    @Test
    void codecEncodeFailure_persistsNothing() throws Exception {
        codec.failEncode.set(true);
        assertThrows(IllegalStateException.class, () -> store.storeWithEvidence(digest("enc"), approval("alice", "t"),
                evidence("alice", "t", NOW, NOW.plus(1, ChronoUnit.MINUTES))));
        assertEquals(0L, count("foundgine_gateway_approvals", "TRUE"));
        assertEquals(0L, count("foundgine_outbox", "TRUE"));
    }

    @Test
    void invalidEvidenceAndInputs_areRejectedBeforeAnyWrite() throws Exception {
        var ok = evidence("alice", "tenant-a", NOW, NOW.plus(1, ChronoUnit.MINUTES));
        var approval = approval("alice", "tenant-a");
        assertThrows(IllegalArgumentException.class, () -> store.storeWithEvidence(digest("a"), approval,
                evidence("bob", "tenant-a", NOW, NOW.plus(1, ChronoUnit.MINUTES))), "subject mismatch");
        assertThrows(IllegalArgumentException.class, () -> store.storeWithEvidence(digest("b"), approval,
                evidence("alice", "tenant-b", NOW, NOW.plus(1, ChronoUnit.MINUTES))), "tenant mismatch");
        assertThrows(IllegalArgumentException.class, () -> store.storeWithEvidence(digest("c"), approval,
                evidence("alice", "tenant-a", NOW, NOW)), "empty validity interval");
        assertThrows(IllegalArgumentException.class, () -> store.storeWithEvidence(digest("d"), approval,
                new FoundgineGatewayApprovalEvidence(ok.eventId(), "Other", "alice", "tenant-a", "fp", NOW, ok.expiresAt())),
                "wrong event type");
        assertThrows(IllegalArgumentException.class, () -> store.storeWithEvidence(digest("e"), approval,
                new FoundgineGatewayApprovalEvidence(ok.eventId(), "ApprovalRequested", "alice", "tenant-a", "other-fp", NOW, ok.expiresAt())),
                "fingerprint mismatch");
        assertThrows(IllegalArgumentException.class, () -> store.storeWithEvidence("not-a-digest", approval, ok), "bad digest");
        assertThrows(IllegalArgumentException.class, () -> store.storeWithEvidence(digest("f"), approval,
                new FoundgineGatewayApprovalEvidence("not-a-uuid", "ApprovalRequested", "alice", "tenant-a", "fp", NOW, ok.expiresAt())),
                "bad event id");
        assertEquals(0L, count("foundgine_gateway_approvals", "TRUE"));
        assertEquals(0L, count("foundgine_outbox", "TRUE"));
    }

    @Test
    void standaloneStoreIsProhibited() {
        assertThrows(UnsupportedOperationException.class,
                () -> store.store(digest("x"), approval("alice", "t"), "alice", "t", NOW.plusSeconds(60)));
    }

    // ------------------------------------------------------------ global tenant partition

    @Test
    void globalApproval_usesReservedOutboxPartitionButKeepsNullTenantInPayload() throws Exception {
        var digest = digest("global");
        var evidence = evidence("alice", null, NOW, NOW.plus(10, ChronoUnit.MINUTES));
        store.storeWithEvidence(digest, approval("alice", null), evidence);

        assertEquals(1L, count("foundgine_outbox", "tenant_id = '" + PostgresFoundgineGatewayApprovalStore.GLOBAL_OUTBOX_PARTITION + "'"));
        assertEquals(1L, count("foundgine_gateway_approvals", "tenant_id IS NULL"));
        try (Connection c = dataSource.getConnection(); Statement s = c.createStatement();
                ResultSet rs = s.executeQuery("SELECT payload->>'tenant' IS NULL, payload->>'subject' FROM foundgine_outbox")) {
            assertTrue(rs.next());
            assertTrue(rs.getBoolean(1), "payload keeps the actual null tenant");
            assertEquals("alice", rs.getString(2));
        }
        assertTrue(store.take(digest, "alice", "", NOW).isEmpty(), "empty-string tenant is not the global scope");
        assertTrue(store.take(digest, "alice", null, NOW).isPresent());
    }

    @Test
    void reservedPartitionIdCannotBeUsedAsARealTenant() throws Exception {
        var reserved = PostgresFoundgineGatewayApprovalStore.GLOBAL_OUTBOX_PARTITION;
        assertThrows(IllegalArgumentException.class, () -> store.storeWithEvidence(digest("res"),
                approval("alice", reserved), evidence("alice", reserved, NOW, NOW.plus(1, ChronoUnit.MINUTES))));
        assertEquals(0L, count("foundgine_gateway_approvals", "TRUE"));
        assertEquals(0L, count("foundgine_outbox", "TRUE"));
    }

    @Test
    void committedEvidenceIsLeasableByTheRealOutboxDispatcherPath() throws Exception {
        var evidence = evidence("alice", "tenant-a", NOW, NOW.plus(10, ChronoUnit.MINUTES));
        store.storeWithEvidence(digest("lease"), approval("alice", "tenant-a"), evidence);
        store.storeWithEvidence(digest("lease-g"), approval("alice", null),
                evidence("alice", null, NOW, NOW.plus(10, ChronoUnit.MINUTES)));

        var outbox = new PostgresOutbox(dataSource);
        List<FoundgineOutboxLease> leases = outbox.lease(10, Duration.ofSeconds(30), NOW.plusSeconds(1));
        assertEquals(2, leases.size());
        assertTrue(leases.stream().allMatch(l -> "ApprovalRequested".equals(l.message().topic())));
        var first = leases.stream().filter(l -> "tenant-a".equals(l.message().tenantId())).findFirst().orElseThrow();
        assertEquals(UUID.fromString(dashed(evidence.eventId())), first.message().id());
    }

    // ------------------------------------------------------------ one-time consumption

    @Test
    void concurrentTake_allowsExactlyOneConsumer() throws Exception {
        var digest = digest("race");
        store.storeWithEvidence(digest, approval("alice", "tenant-a"),
                evidence("alice", "tenant-a", NOW, NOW.plus(10, ChronoUnit.MINUTES)));
        List<Object> results = runConcurrently(16, i -> store.take(digest, "alice", "tenant-a", NOW).isPresent());
        assertEquals(1L, results.stream().filter(Boolean.TRUE::equals).count(), "exactly one consumer wins");
        assertEquals(0L, count("foundgine_gateway_approvals", "TRUE"));
    }

    @Test
    void takeAfterRestart_usesANewlyConstructedStore() throws Exception {
        var digest = digest("restart");
        store.storeWithEvidence(digest, approval("alice", "tenant-a"),
                evidence("alice", "tenant-a", NOW, NOW.plus(10, ChronoUnit.MINUTES)));

        var restarted = new PostgresFoundgineGatewayApprovalStore(dataSource, new TestCodec(), false);
        var taken = restarted.take(digest, "alice", "tenant-a", NOW).orElseThrow();
        assertEquals("approval-alice", taken.approvalId());
        assertEquals("alice", taken.request().security().subject());
        assertEquals("tenant-a", taken.request().security().tenant());
        assertTrue(restarted.take(digest, "alice", "tenant-a", NOW).isEmpty(), "one-time even across instances");
        assertTrue(store.take(digest, "alice", "tenant-a", NOW).isEmpty());
    }

    @Test
    void concurrentTakeAcrossIndependentStoreInstances_allowsExactlyOneConsumer() throws Exception {
        var digest = digest("race-multi");
        store.storeWithEvidence(digest, approval("alice", null), evidence("alice", null, NOW, NOW.plus(10, ChronoUnit.MINUTES)));
        List<Object> results = runConcurrently(12, i ->
                new PostgresFoundgineGatewayApprovalStore(dataSource, new TestCodec(), false).take(digest, "alice", null, NOW).isPresent());
        assertEquals(1L, results.stream().filter(Boolean.TRUE::equals).count());
    }

    // ------------------------------------------------------------ isolation

    @Test
    void subjectAndTenantIsolation_wrongCallersNeitherSeeNorConsume() throws Exception {
        var digest = digest("iso");
        store.storeWithEvidence(digest, approval("alice", "tenant-a"),
                evidence("alice", "tenant-a", NOW, NOW.plus(10, ChronoUnit.MINUTES)));

        assertTrue(store.take(digest, "bob", "tenant-a", NOW).isEmpty());
        assertTrue(store.take(digest, "alice", "tenant-b", NOW).isEmpty());
        assertTrue(store.take(digest, "alice", null, NOW).isEmpty());
        assertTrue(store.take(digest, "ALICE", "tenant-a", NOW).isEmpty(), "subject comparison is case-sensitive");
        assertFalse(store.revoke(digest, "bob", "tenant-a"));
        assertFalse(store.revoke(digest, "alice", "tenant-b"));
        assertEquals(1L, count("foundgine_gateway_approvals", "TRUE"), "failed attempts must not consume or revoke");
        assertTrue(store.take(digest, "alice", "tenant-a", NOW).isPresent());
    }

    @Test
    void sameSubjectInDifferentTenants_doesNotCrossConsume() throws Exception {
        var a = digest("tenant-a");
        var b = digest("tenant-b");
        store.storeWithEvidence(a, approval("alice", "tenant-a"), evidence("alice", "tenant-a", NOW, NOW.plus(1, ChronoUnit.MINUTES)));
        store.storeWithEvidence(b, approval("alice", "tenant-b"), evidence("alice", "tenant-b", NOW, NOW.plus(1, ChronoUnit.MINUTES)));
        assertTrue(store.take(a, "alice", "tenant-b", NOW).isEmpty());
        assertTrue(store.take(b, "alice", "tenant-a", NOW).isEmpty());
        assertEquals("tenant-a", store.take(a, "alice", "tenant-a", NOW).orElseThrow().request().security().tenant());
        assertEquals("tenant-b", store.take(b, "alice", "tenant-b", NOW).orElseThrow().request().security().tenant());
    }

    @Test
    void malformedLookupInputs_neverReachOrConsumeRows() throws Exception {
        var digest = digest("malformed");
        store.storeWithEvidence(digest, approval("alice", "t"), evidence("alice", "t", NOW, NOW.plus(1, ChronoUnit.MINUTES)));
        assertTrue(store.take(null, "alice", "t", NOW).isEmpty());
        assertTrue(store.take("' OR '1'='1", "alice", "t", NOW).isEmpty());
        assertTrue(store.take(digest, null, "t", NOW).isEmpty());
        assertFalse(store.revoke("' OR '1'='1", "alice", "t"));
        assertEquals(1L, count("foundgine_gateway_approvals", "TRUE"));
    }

    // ------------------------------------------------------------ expiry / revocation / codec recovery

    @Test
    void expiry_isExclusiveAtTheBoundaryAndExpiredRowsAreNeverConsumed() throws Exception {
        var expires = NOW.plus(10, ChronoUnit.MINUTES);
        var digest = digest("expiry");
        store.storeWithEvidence(digest, approval("alice", "t"), evidence("alice", "t", NOW, expires));

        assertTrue(store.take(digest, "alice", "t", expires).isEmpty(), "expires_at == now is expired");
        assertTrue(store.take(digest, "alice", "t", expires.plusSeconds(1)).isEmpty());
        assertEquals(1L, count("foundgine_gateway_approvals", "TRUE"), "expired rows stay until purged");
        assertTrue(store.take(digest, "alice", "t", expires.minusMillis(1)).isPresent(), "valid just before the boundary");
    }

    @Test
    void databaseClockRejectsApprovalEvenWhenCallerClockIsBehind() throws Exception {
        Instant databaseNow = Instant.now();
        Instant callerNow = databaseNow.minus(5, ChronoUnit.MINUTES);
        Instant expiredAt = databaseNow.minus(1, ChronoUnit.MINUTES);
        var digest = digest("database-clock");
        store.storeWithEvidence(digest, approval("alice", "t"),
                evidence("alice", "t", databaseNow.minus(10, ChronoUnit.MINUTES), expiredAt));

        var databaseClockStore = new PostgresFoundgineGatewayApprovalStore(dataSource, codec);
        assertTrue(databaseClockStore.take(digest, "alice", "t", callerNow).isEmpty(),
                "database time must prevent a slow/incorrect caller clock from extending approval validity");
        assertEquals(1L, count("foundgine_gateway_approvals", "TRUE"),
                "an expired approval is not returned to the caller");
    }

    @Test
    void purgeExpired_removesOnlyExpiredRowsInBatches() throws Exception {
        for (int i = 0; i < 5; i++)
            store.storeWithEvidence(digest("old" + i), approval("alice", "t"),
                    evidence("alice", "t", NOW, NOW.plus(1 + i, ChronoUnit.MINUTES)));
        var live = digest("live");
        store.storeWithEvidence(live, approval("alice", "t"), evidence("alice", "t", NOW, NOW.plus(1, ChronoUnit.HOURS)));

        var cutoff = NOW.plus(10, ChronoUnit.MINUTES);
        assertEquals(2, store.purgeExpired(cutoff, 2));
        assertEquals(3, store.purgeExpired(cutoff, 100));
        assertEquals(0, store.purgeExpired(cutoff, 100));
        assertEquals(1L, count("foundgine_gateway_approvals", "TRUE"));
        assertTrue(store.take(live, "alice", "t", cutoff).isPresent());
        assertThrows(IllegalArgumentException.class, () -> store.purgeExpired(cutoff, 0));
    }

    @Test
    void revocation_isOwnerBoundOneTimeAndPreventsConsumption() throws Exception {
        var digest = digest("revoke");
        store.storeWithEvidence(digest, approval("alice", "t"), evidence("alice", "t", NOW, NOW.plus(1, ChronoUnit.MINUTES)));
        assertFalse(store.revoke(digest, "bob", "t"));
        assertTrue(store.revoke(digest, "alice", "t"));
        assertFalse(store.revoke(digest, "alice", "t"));
        assertTrue(store.take(digest, "alice", "t", NOW).isEmpty());
        assertEquals(1L, count("foundgine_outbox", "TRUE"), "revocation does not rewrite committed evidence");
    }

    @Test
    void codecDecodeFailure_rollsBackConsumptionAndAllowsRetry() throws Exception {
        var digest = digest("decode");
        store.storeWithEvidence(digest, approval("alice", "t"), evidence("alice", "t", NOW, NOW.plus(10, ChronoUnit.MINUTES)));

        codec.failDecode.set(true);
        assertThrows(IllegalStateException.class, () -> store.take(digest, "alice", "t", NOW));
        assertEquals(1L, count("foundgine_gateway_approvals", "TRUE"), "failed decode must not consume the approval");

        codec.failDecode.set(false);
        assertTrue(store.take(digest, "alice", "t", NOW).isPresent(), "approval survives for a retry");
        assertTrue(store.take(digest, "alice", "t", NOW).isEmpty());
    }

    @Test
    void slowCodecDecodeDoesNotHoldApprovalRowLockOrTransaction() throws Exception {
        var digest = digest("slow-decode");
        store.storeWithEvidence(digest, approval("alice", "t"),
                evidence("alice", "t", NOW, NOW.plus(10, ChronoUnit.MINUTES)));
        codec.decodeStarted = new CountDownLatch(1);
        codec.releaseDecode = new CountDownLatch(1);
        var pool = Executors.newSingleThreadExecutor();
        try {
            Future<Boolean> taker = pool.submit(() -> store.take(digest, "alice", "t", NOW).isPresent());
            assertTrue(codec.decodeStarted.await(5, java.util.concurrent.TimeUnit.SECONDS),
                    "codec must be reached after the initial unlocked read");
            try (Connection c = dataSource.getConnection()) {
                c.setAutoCommit(false);
                try (PreparedStatement p = c.prepareStatement(
                        "SELECT handle_digest FROM foundgine_gateway_approvals WHERE handle_digest=? FOR UPDATE NOWAIT")) {
                    p.setString(1, digest);
                    try (ResultSet rs = p.executeQuery()) {
                        assertTrue(rs.next(), "the approval row must remain available while decoding is in progress");
                    }
                }
                c.commit();
            }
            codec.releaseDecode.countDown();
            assertTrue(taker.get(10, java.util.concurrent.TimeUnit.SECONDS));
        } finally {
            codec.releaseDecode.countDown();
            pool.shutdownNow();
            codec.decodeStarted = null;
            codec.releaseDecode = null;
        }
    }

    @Test
    void decodeFailureDoesNotLetAConcurrentConsumerDoubleSpend() throws Exception {
        var digest = digest("decode-race");
        store.storeWithEvidence(digest, approval("alice", "t"), evidence("alice", "t", NOW, NOW.plus(10, ChronoUnit.MINUTES)));
        codec.failFirstDecodes.set(3);
        List<Object> results = runConcurrently(8, i -> {
            try {
                return store.take(digest, "alice", "t", NOW).isPresent();
            } catch (IllegalStateException expected) {
                return null; // injected codec failure, rolled back
            }
        });
        assertEquals(1L, results.stream().filter(Boolean.TRUE::equals).count(), "at most one successful consumption");
        assertEquals(0L, count("foundgine_gateway_approvals", "TRUE"));
    }

    // ------------------------------------------------------------ helpers

    private static String digest(String seed) {
        try {
            return HexFormat.of().withUpperCase().formatHex(
                    MessageDigest.getInstance("SHA-256").digest(seed.getBytes(java.nio.charset.StandardCharsets.UTF_8)));
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }

    private static String dashed(String n) {
        return n.substring(0, 8) + "-" + n.substring(8, 12) + "-" + n.substring(12, 16) + "-" + n.substring(16, 20) + "-" + n.substring(20);
    }

    private static FoundgineGatewayApprovalEvidence evidence(String subject, String tenant, Instant at, Instant expires) {
        return new FoundgineGatewayApprovalEvidence(UUID.randomUUID().toString().replace("-", ""),
                "ApprovalRequested", subject, tenant, "fp", at, expires);
    }

    private static SecurityExecutionContext ctx(String subject, String tenant) {
        var warrant = SecurityWarrant.ofDefaults("w", "iss", subject, "aud", List.of(),
                SecurityWarrantConstraints.UNRESTRICTED, Instant.EPOCH, Instant.MAX, "n", "k", null, null);
        return new SecurityExecutionContext(warrant, subject, "aud", tenant, null);
    }

    private static MutationPlanApproval approval(String subject, String tenant) {
        var graph = new SemanticMutationOperationGraph(List.of(new SemanticMutationOperation(
                new EntityId(1L), SemanticMutationKind.CREATE, null, null, null, null, null, null)));
        return new MutationPlanApproval(new SemanticMutationRequest(graph, ctx(subject, tenant)),
                "approval-" + subject, "fp", subject, NOW);
    }

    /** Deterministic JSON codec; payloads are plain JSON here, hosts are expected to encrypt. */
    private static final class TestCodec implements FoundgineGatewayApprovalPayloadCodec {
        final AtomicBoolean failEncode = new AtomicBoolean();
        final AtomicBoolean failDecode = new AtomicBoolean();
        final java.util.concurrent.atomic.AtomicInteger failFirstDecodes = new java.util.concurrent.atomic.AtomicInteger();
        volatile CountDownLatch decodeStarted;
        volatile CountDownLatch releaseDecode;

        @Override public String encodeApproval(MutationPlanApproval a) {
            if (failEncode.get()) throw new IllegalStateException("injected encode failure");
            var security = a.request().security();
            return write(Map.of("approvalId", a.approvalId(), "fingerprint", a.planFingerprint(), "approvedBy", a.approvedBy(),
                    "subject", security.subject(), "tenant", security.tenant() == null ? "" : security.tenant(),
                    "hasTenant", security.tenant() != null));
        }

        @Override public MutationPlanApproval decodeApproval(String payload) {
            var started = decodeStarted;
            var release = releaseDecode;
            if (started != null) {
                started.countDown();
                try {
                    if (release != null && !release.await(10, java.util.concurrent.TimeUnit.SECONDS))
                        throw new IllegalStateException("timed out waiting to release test codec");
                } catch (InterruptedException e) {
                    Thread.currentThread().interrupt();
                    throw new IllegalStateException("test codec interrupted", e);
                }
            }
            if (failDecode.get() || failFirstDecodes.getAndUpdate(n -> n > 0 ? n - 1 : 0) > 0)
                throw new IllegalStateException("injected decode failure");
            try {
                var node = JSON.readTree(payload);
                String tenant = node.get("hasTenant").asBoolean() ? node.get("tenant").asText() : null;
                var graph = new SemanticMutationOperationGraph(List.of(new SemanticMutationOperation(
                        new EntityId(1L), SemanticMutationKind.CREATE, null, null, null, null, null, null)));
                return new MutationPlanApproval(new SemanticMutationRequest(graph, ctx(node.get("subject").asText(), tenant)),
                        node.get("approvalId").asText(), node.get("fingerprint").asText(), node.get("approvedBy").asText(), NOW);
            } catch (Exception e) {
                throw new IllegalStateException(e);
            }
        }

        @Override public String encodeEvidence(FoundgineGatewayApprovalEvidence e) {
            var m = new java.util.LinkedHashMap<String, Object>();
            m.put("eventId", e.eventId()); m.put("eventType", e.eventType()); m.put("subject", e.subject());
            m.put("tenant", e.tenant()); m.put("planFingerprint", e.planFingerprint());
            m.put("occurredAt", e.occurredAt().toString()); m.put("expiresAt", e.expiresAt().toString());
            return write(m);
        }

        private static String write(Object value) {
            try {
                return JSON.writeValueAsString(value);
            } catch (Exception e) {
                throw new IllegalStateException(e);
            }
        }
    }

    private long count(String table, String where) throws SQLException {
        try (Connection c = dataSource.getConnection(); Statement s = c.createStatement();
                ResultSet rs = s.executeQuery("SELECT count(*) FROM " + table + " WHERE " + where)) {
            rs.next();
            return rs.getLong(1);
        }
    }

    private <T> List<Object> runConcurrently(int threads, ThrowingFunction<Integer, T> task) throws Exception {
        ExecutorService pool = Executors.newFixedThreadPool(threads);
        try {
            var ready = new CountDownLatch(threads);
            var go = new CountDownLatch(1);
            List<Future<Object>> futures = new ArrayList<>();
            for (int i = 0; i < threads; i++) {
                final int n = i;
                Callable<Object> c = () -> {
                    ready.countDown();
                    go.await();
                    return task.apply(n);
                };
                futures.add(pool.submit(c));
            }
            assertTrue(ready.await(30, java.util.concurrent.TimeUnit.SECONDS));
            go.countDown();
            List<Object> results = new ArrayList<>();
            for (var f : futures) results.add(f.get(60, java.util.concurrent.TimeUnit.SECONDS));
            return results;
        } finally {
            pool.shutdownNow();
        }
    }

    @FunctionalInterface
    private interface ThrowingFunction<A, R> {
        R apply(A a) throws Exception;
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
                parts.put(item.substring(0, eq).trim().toLowerCase(java.util.Locale.ROOT), item.substring(eq + 1).trim());
        }
        return "jdbc:postgresql://" + parts.getOrDefault("host", "localhost") + ":" + parts.getOrDefault("port", "5432")
                + "/" + parts.getOrDefault("database", "postgres") + "?user="
                + parts.getOrDefault("username", parts.getOrDefault("user", "postgres")) + "&password="
                + parts.getOrDefault("password", "");
    }
}
