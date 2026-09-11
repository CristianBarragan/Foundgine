package com.foundgine.benchmarks.highassurance.postgres;

import com.foundgine.benchmarks.highassurance.TransferFunds;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.Test;

import java.math.BigDecimal;
import java.nio.charset.StandardCharsets;
import java.sql.*;
import java.time.Duration;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

import static org.junit.jupiter.api.Assertions.*;

/**
 * PostgreSQL concurrency/visibility parity for the C# HighAssurance fixture.
 * These tests are intentionally opt-in because they require a live PostgreSQL server.
 */
class PostgresTransferFundsConcurrencyParityTest {
    private String url() {
        var value = System.getenv("FOUNDGINE_POSTGRES_CONNECTION_STRING");
        Assumptions.assumeTrue(value != null && !value.isBlank(),
                "FOUNDGINE_POSTGRES_CONNECTION_STRING is not configured");
        return value;
    }

    @Test
    void sameIdempotencyKeyFaultRollbackAllowsWaitingRequestToExecuteOnce() throws Exception {
        var url = url();
        UUID actor = UUID.randomUUID(), source = UUID.randomUUID(), destination = UUID.randomUUID();
        prepare(url, 207, actor, source, destination, 1000, 0, false, 2000, 2000, 0, 0);

        var entered = new CountDownLatch(1);
        var release = new CountDownLatch(1);
        var inject = new AtomicBoolean(true);
        var firstExecutor = new PostgresTransferFundsExecutor(url, new Properties(),
                (id, a, b) -> new PostgresAuthorizationDecision(true, 1, "authorization-v1"),
                (point, connection) -> {
                    if (point != PostgresTransferFundsExecutor.FaultPoint.AFTER_MUTATION_BEFORE_COMMIT || !inject.compareAndSet(true, false)) return;
                    entered.countDown();
                    try { release.await(10, TimeUnit.SECONDS); } catch (InterruptedException e) { Thread.currentThread().interrupt(); throw new IllegalStateException(e); }
                    throw new IllegalStateException("Injected concurrency fault before commit.");
                });
        var waiting = service(url, (id, a, b) -> new PostgresAuthorizationDecision(id.equals(a.ownerId()) && id.equals(b.ownerId()), 1, "authorization-v1"));
        var first = new PostgresTransferFundsService(firstExecutor);
        var command = new TransferFunds.Command(source, destination, bd("100"), "fault-concurrency-same-key");
        var failed = CompletableFuture.supplyAsync(() -> executeUnchecked(first, actor, 207, command));
        assertTrue(entered.await(5, TimeUnit.SECONDS));
        var replayCandidate = CompletableFuture.supplyAsync(() -> executeUnchecked(waiting, actor, 207, command));
        Thread.sleep(250);
        assertFalse(replayCandidate.isDone(), "The second request should remain blocked by the first transaction's advisory lock.");
        release.countDown();
        assertThrows(CompletionException.class, failed::join);
        var receipt = replayCandidate.get(10, TimeUnit.SECONDS);
        assertFalse(receipt.replay());
        assertState(url, source, destination, "900.0000", "100.0000", 1, 1);
    }

    @Test
    void opposingTransferWaitsThroughFaultThenCommitsWithoutPartialState() throws Exception {
        var url = url();
        UUID actor = UUID.randomUUID(), a = UUID.randomUUID(), b = UUID.randomUUID();
        prepare(url, 208, actor, a, b, 1000, 1000, false, 5000, 5000, 0, 0);
        var entered = new CountDownLatch(1);
        var release = new CountDownLatch(1);
        var inject = new AtomicBoolean(true);
        var firstExecutor = new PostgresTransferFundsExecutor(url, new Properties(),
                (id, x, y) -> new PostgresAuthorizationDecision(true, 1, "authorization-v1"),
                (point, connection) -> {
                    if (point != PostgresTransferFundsExecutor.FaultPoint.AFTER_MUTATION_BEFORE_COMMIT || !inject.compareAndSet(true, false)) return;
                    entered.countDown();
                    try { release.await(10, TimeUnit.SECONDS); } catch (InterruptedException e) { Thread.currentThread().interrupt(); throw new IllegalStateException(e); }
                    throw new IllegalStateException("Injected opposing-transfer fault before commit.");
                });
        var first = new PostgresTransferFundsService(firstExecutor);
        var second = service(url, (id, x, y) -> new PostgresAuthorizationDecision(id.equals(x.ownerId()) && id.equals(y.ownerId()), 1, "authorization-v1"));
        var failed = CompletableFuture.supplyAsync(() -> executeUnchecked(first, actor, 208, new TransferFunds.Command(a, b, bd("100"), "fault-a-to-b")));
        assertTrue(entered.await(5, TimeUnit.SECONDS));
        var waiting = CompletableFuture.supplyAsync(() -> executeUnchecked(second, actor, 208, new TransferFunds.Command(b, a, bd("200"), "wait-b-to-a")));
        Thread.sleep(250);
        assertFalse(waiting.isDone(), "The opposing transfer should wait for the locked account rows.");
        release.countDown();
        assertThrows(CompletionException.class, failed::join);
        var receipt = waiting.get(10, TimeUnit.SECONDS);
        assertFalse(receipt.replay());
        assertState(url, a, b, "1200.0000", "800.0000", 1, 1);
    }

    @Test
    void authorizationRevokedWhileTransferWaitsIsObservedAfterLockAcquisition() throws Exception {
        var url = url();
        UUID actor = UUID.randomUUID(), source = UUID.randomUUID(), destination = UUID.randomUUID();
        prepare(url, 209, actor, source, destination, 1000, 1000, false, 5000, 5000, 0, 0);
        try (var blocker = DriverManager.getConnection(url)) {
            blocker.setAutoCommit(false); lockAccounts(blocker, source, destination);
            var revoked = new AtomicBoolean(false);
            var observed = new AtomicBoolean(false);
            var executor = new PostgresTransferFundsExecutor(url, new Properties(), (id, a, b) -> {
                if (revoked.get()) observed.set(true);
                return new PostgresAuthorizationDecision(!revoked.get(), revoked.get() ? 2 : 1, revoked.get() ? "authorization-v2" : "authorization-v1");
            });
            var service = new PostgresTransferFundsService(executor);
            var transfer = CompletableFuture.supplyAsync(() -> executeUnchecked(service, actor, 209,
                    new TransferFunds.Command(source, destination, bd("100"), "visibility-authorization-context-race")));
            Thread.sleep(100); assertFalse(transfer.isDone());
            revoked.set(true);
            blocker.commit();
            var failure = assertThrows(CompletionException.class, transfer::join);
            assertInstanceOf(SemanticAuthorizationException.class, rootCause(failure));
            assertTrue(observed.get(), "Authorization must be evaluated after the transfer acquires authoritative locks.");
        }
        assertState(url, source, destination, "1000.0000", "1000.0000", 0, 0);
    }

    @Test
    void sameIdempotencyKeyExecutesExactlyOnce() throws Exception {
        var url = url();
        UUID actor = UUID.randomUUID(), source = UUID.randomUUID(), destination = UUID.randomUUID();
        prepare(url, 200, actor, source, destination, 1000, 0, false, 2000, 2000, 0, 0);

        var service = service(url, (id, a, b) -> new PostgresAuthorizationDecision(id.equals(a.ownerId()) && id.equals(b.ownerId()), 1, "authorization-v1"));
        var command = new TransferFunds.Command(source, destination, bd("100"), "same-key-concurrency");
        var pool = Executors.newFixedThreadPool(8);
        try {
            var futures = new ArrayList<Future<PostgresTransferFundsService.Receipt>>();
            for (int i = 0; i < 8; i++) futures.add(pool.submit(() -> service.execute(actor, 200, command)));
            var receipts = new ArrayList<PostgresTransferFundsService.Receipt>();
            for (var f : futures) receipts.add(f.get(10, TimeUnit.SECONDS));

            assertEquals(1, receipts.stream().map(PostgresTransferFundsService.Receipt::transferId).distinct().count());
            assertEquals(1, receipts.stream().filter(r -> !r.replay()).count());
            assertEquals(7, receipts.stream().filter(PostgresTransferFundsService.Receipt::replay).count());
            assertState(url, source, destination, "900.0000", "100.0000", 1, 1);
        } finally { pool.shutdownNow(); }
    }

    @Test
    void opposingTransfersAreSerializedWithoutDeadlock() throws Exception {
        var url = url();
        UUID actor = UUID.randomUUID(), a = UUID.randomUUID(), b = UUID.randomUUID();
        prepare(url, 201, actor, a, b, 1000, 1000, false, 5000, 5000, 0, 0);
        var service = service(url, (id, x, y) -> new PostgresAuthorizationDecision(id.equals(x.ownerId()) && id.equals(y.ownerId()), 1, "authorization-v1"));
        var pool = Executors.newFixedThreadPool(2);
        try {
            var first = pool.submit(() -> service.execute(actor, 201, new TransferFunds.Command(a, b, bd("100"), "a-to-b")));
            var second = pool.submit(() -> service.execute(actor, 201, new TransferFunds.Command(b, a, bd("200"), "b-to-a")));
            var r1 = first.get(10, TimeUnit.SECONDS);
            var r2 = second.get(10, TimeUnit.SECONDS);
            assertFalse(r1.replay()); assertFalse(r2.replay());
            assertState(url, a, b, "1100.0000", "900.0000", 2, 2);
        } finally { pool.shutdownNow(); }
    }

    @Test
    void ownershipChangeQueuedBeforeTransferLockIsVisibleAfterLockAcquisition() throws Exception {
        var url = url();
        UUID actor = UUID.randomUUID(), newOwner = UUID.randomUUID(), source = UUID.randomUUID(), destination = UUID.randomUUID();
        prepare(url, 202, actor, source, destination, 1000, 1000, false, 5000, 5000, 0, 0);

        try (var blocker = DriverManager.getConnection(url)) {
            blocker.setAutoCommit(false);
            lockAccounts(blocker, source, destination);
            try (var ownerConnection = DriverManager.getConnection(url)) {
                ownerConnection.setAutoCommit(false);
                var ownerUpdate = CompletableFuture.runAsync(() -> changeOwner(ownerConnection, source, newOwner));
                Thread.sleep(100);
                var service = service(url, (id, a, b) -> new PostgresAuthorizationDecision(id.equals(a.ownerId()) && id.equals(b.ownerId()), 1, "authorization-v1"));
                var transfer = CompletableFuture.supplyAsync(() -> executeUnchecked(service, actor, 202,
                        new TransferFunds.Command(source, destination, bd("100"), "visibility-owner-race")));
                Thread.sleep(100);
                assertFalse(transfer.isDone(), "Transfer should wait for authoritative account locks");
                blocker.commit();
                ownerUpdate.get(10, TimeUnit.SECONDS);
                ownerConnection.commit();
                var failure = assertThrows(CompletionException.class, () -> transfer.join());
                assertInstanceOf(SemanticAuthorizationException.class, rootCause(failure));
            }
        }
        assertState(url, source, destination, "1000.0000", "1000.0000", 0, 0);
        assertEquals(newOwner, readUuid(url, "SELECT owner_id FROM banking.bank_account WHERE id=?", source));
    }

    @Test
    void frozenStateCommittedBeforeTransferLockIsObserved() throws Exception {
        var url = url();
        UUID actor = UUID.randomUUID(), source = UUID.randomUUID(), destination = UUID.randomUUID();
        prepare(url, 203, actor, source, destination, 1000, 1000, false, 5000, 5000, 0, 0);
        try (var blocker = DriverManager.getConnection(url)) {
            blocker.setAutoCommit(false); lockAccounts(blocker, source, destination);
            try (var state = DriverManager.getConnection(url)) {
                state.setAutoCommit(false);
                var freeze = CompletableFuture.runAsync(() -> setFrozen(state, destination));
                Thread.sleep(100);
                var service = service(url, (id, a, b) -> new PostgresAuthorizationDecision(id.equals(a.ownerId()) && id.equals(b.ownerId()), 1, "authorization-v1"));
                var transfer = CompletableFuture.supplyAsync(() -> executeUnchecked(service, actor, 203,
                        new TransferFunds.Command(source, destination, bd("100"), "visibility-frozen-race")));
                Thread.sleep(100); assertFalse(transfer.isDone());
                blocker.commit(); freeze.get(10, TimeUnit.SECONDS); state.commit();
                var failure = assertThrows(CompletionException.class, () -> transfer.join());
                assertInstanceOf(IllegalStateException.class, rootCause(failure));
            }
        }
        assertState(url, source, destination, "1000.0000", "1000.0000", 0, 0);
    }

    @Test
    void tenantReassignmentCommittedBeforeTransferLockBlocksCrossTenantTransfer() throws Exception {
        var url = url();
        UUID actor = UUID.randomUUID(), source = UUID.randomUUID(), destination = UUID.randomUUID();
        prepare(url, 204, actor, source, destination, 1000, 1000, false, 5000, 5000, 0, 0);
        try (var blocker = DriverManager.getConnection(url)) {
            blocker.setAutoCommit(false); lockAccounts(blocker, source, destination);
            try (var tenant = DriverManager.getConnection(url)) {
                tenant.setAutoCommit(false);
                var change = CompletableFuture.runAsync(() -> changeTenant(tenant, source, 205));
                Thread.sleep(100);
                var service = service(url, (id, a, b) -> new PostgresAuthorizationDecision(id.equals(a.ownerId()) && id.equals(b.ownerId()), 1, "authorization-v1"));
                var transfer = CompletableFuture.supplyAsync(() -> executeUnchecked(service, actor, 204,
                        new TransferFunds.Command(source, destination, bd("100"), "visibility-tenant-race")));
                Thread.sleep(100); assertFalse(transfer.isDone());
                blocker.commit(); change.get(10, TimeUnit.SECONDS); tenant.commit();
                var failure = assertThrows(CompletionException.class, () -> transfer.join());
                assertInstanceOf(IllegalStateException.class, rootCause(failure));
            }
        }
        assertState(url, source, destination, "1000.0000", "1000.0000", 0, 0);
        assertEquals(205, readInt(url, "SELECT tenant_id FROM banking.bank_account WHERE id=?", source));
    }

    @Test
    void accountDeletedBeforeTransferLockCannotCrossMissingAccountBoundary() throws Exception {
        var url = url();
        UUID actor = UUID.randomUUID(), source = UUID.randomUUID(), destination = UUID.randomUUID();
        prepare(url, 206, actor, source, destination, 1000, 1000, false, 5000, 5000, 0, 0);
        try (var blocker = DriverManager.getConnection(url)) {
            blocker.setAutoCommit(false); lockAccounts(blocker, source, destination);
            try (var deletion = DriverManager.getConnection(url)) {
                deletion.setAutoCommit(false);
                var delete = CompletableFuture.runAsync(() -> deleteAccount(deletion, source));
                Thread.sleep(100);
                var service = service(url, (id, a, b) -> new PostgresAuthorizationDecision(id.equals(a.ownerId()) && id.equals(b.ownerId()), 1, "authorization-v1"));
                var transfer = CompletableFuture.supplyAsync(() -> executeUnchecked(service, actor, 206,
                        new TransferFunds.Command(source, destination, bd("100"), "visibility-delete-race")));
                Thread.sleep(100); assertFalse(transfer.isDone());
                blocker.commit(); delete.get(10, TimeUnit.SECONDS); deletion.commit();
                var failure = assertThrows(CompletionException.class, () -> transfer.join());
                assertInstanceOf(IllegalStateException.class, rootCause(failure));
            }
        }
        assertEquals(0, count(url, "bank_account", source));
        assertEquals(0, count(url, "transfer_idempotency"));
        assertEquals(0, count(url, "transfer_audit"));
    }

    private static PostgresTransferFundsService service(String url, PostgresTransferFundsExecutor.Authorization auth) {
        return new PostgresTransferFundsService(new PostgresTransferFundsExecutor(url, new Properties(), auth));
    }

    private static PostgresTransferFundsService.Receipt executeUnchecked(PostgresTransferFundsService s, UUID actor, int tenant, TransferFunds.Command c) {
        try { return s.execute(actor, tenant, c); }
        catch (Exception e) { throw new CompletionException(e); }
    }

    private static Throwable rootCause(Throwable t) {
        var current = t;
        while (current.getCause() != null) current = current.getCause();
        return current;
    }

    private static void prepare(String url, int tenant, UUID actor, UUID source, UUID destination,
                                double sourceBalance, double destinationBalance, boolean frozen,
                                double sourceLimit, double destinationLimit,
                                double pending, double hold) throws Exception {
        try (var c = DriverManager.getConnection(url); var st = c.createStatement()) {
            var schema = PostgresTransferFundsConcurrencyParityTest.class.getResourceAsStream("/high-assurance-postgres-schema.sql");
            assertNotNull(schema);
            st.execute(new String(schema.readAllBytes(), StandardCharsets.UTF_8));
            st.executeUpdate("TRUNCATE banking.transfer_audit, banking.transfer_idempotency, banking.authorization_context, banking.bank_account CASCADE");
            try (var ps = c.prepareStatement("INSERT INTO banking.bank_account(id,tenant_id,owner_id,balance,pending_transactions,regulatory_hold,daily_limit,is_frozen) VALUES (?,?,?,?,?,?,?,?)")) {
                account(ps, source, tenant, actor, sourceBalance, pending, hold, sourceLimit, false);
                account(ps, destination, tenant, actor, destinationBalance, 0, 0, destinationLimit, frozen);
            }
            try (var ps = c.prepareStatement("INSERT INTO banking.authorization_context(actor_id,tenant_id,allowed,version,fingerprint,integrity_algorithm,integrity_key_id,integrity_tag) VALUES (?,?,?,?,?,'HMAC-SHA256/v1','test',repeat('0',64))")) {
                ps.setObject(1, actor); ps.setInt(2, tenant); ps.setBoolean(3, true); ps.setLong(4, 1); ps.setString(5, "authorization-v1"); ps.executeUpdate();
            }
        }
    }

    private static void account(PreparedStatement ps, UUID id, int tenant, UUID owner, double balance, double pending,
                                double hold, double limit, boolean frozen) throws SQLException {
        ps.setObject(1,id); ps.setInt(2,tenant); ps.setObject(3,owner); ps.setBigDecimal(4,bd(Double.toString(balance)));
        ps.setBigDecimal(5,bd(Double.toString(pending))); ps.setBigDecimal(6,bd(Double.toString(hold))); ps.setBigDecimal(7,bd("0"));
        ps.setBigDecimal(8,bd(Double.toString(limit))); ps.setBoolean(9,frozen); ps.executeUpdate();
    }

    private static void lockAccounts(Connection c, UUID source, UUID destination) {
        try (var ps = c.prepareStatement("SELECT id FROM banking.bank_account WHERE id IN (?,?) ORDER BY id FOR UPDATE")) {
            ps.setObject(1, source); ps.setObject(2, destination); ps.executeQuery().close();
        } catch (SQLException e) { throw new CompletionException(e); }
    }
    private static void changeOwner(Connection c, UUID id, UUID owner) {
        try (var ps = c.prepareStatement("UPDATE banking.bank_account SET owner_id=? WHERE id=?")) { ps.setObject(1,owner); ps.setObject(2,id); ps.executeUpdate(); }
        catch (SQLException e) { throw new CompletionException(e); }
    }
    private static void setFrozen(Connection c, UUID id) {
        try (var ps = c.prepareStatement("UPDATE banking.bank_account SET is_frozen=true WHERE id=?")) { ps.setObject(1,id); ps.executeUpdate(); }
        catch (SQLException e) { throw new CompletionException(e); }
    }
    private static void changeTenant(Connection c, UUID id, int tenant) {
        try (var ps = c.prepareStatement("UPDATE banking.bank_account SET tenant_id=? WHERE id=?")) { ps.setInt(1,tenant); ps.setObject(2,id); ps.executeUpdate(); }
        catch (SQLException e) { throw new CompletionException(e); }
    }
    private static void deleteAccount(Connection c, UUID id) {
        try (var ps = c.prepareStatement("DELETE FROM banking.bank_account WHERE id=?")) { ps.setObject(1,id); ps.executeUpdate(); }
        catch (SQLException e) { throw new CompletionException(e); }
    }

    private static void assertState(String url, UUID source, UUID destination, String sourceBalance, String destinationBalance, int idempotency, int audit) throws Exception {
        assertEquals(bd(sourceBalance), readDecimal(url, "SELECT balance FROM banking.bank_account WHERE id=?", source));
        assertEquals(bd(destinationBalance), readDecimal(url, "SELECT balance FROM banking.bank_account WHERE id=?", destination));
        assertEquals(idempotency, count(url, "transfer_idempotency")); assertEquals(audit, count(url, "transfer_audit"));
    }
    private static UUID readUuid(String url, String sql, UUID id) throws Exception { try (var c=DriverManager.getConnection(url); var ps=c.prepareStatement(sql)){ps.setObject(1,id);try(var rs=ps.executeQuery()){assertTrue(rs.next());return rs.getObject(1,UUID.class);}} }
    private static int readInt(String url, String sql, UUID id) throws Exception { try (var c=DriverManager.getConnection(url); var ps=c.prepareStatement(sql)){ps.setObject(1,id);try(var rs=ps.executeQuery()){assertTrue(rs.next());return rs.getInt(1);}} }
    private static BigDecimal readDecimal(String url, String sql, UUID id) throws Exception { try (var c=DriverManager.getConnection(url); var ps=c.prepareStatement(sql)){ps.setObject(1,id);try(var rs=ps.executeQuery()){assertTrue(rs.next());return rs.getBigDecimal(1);}} }
    private static int count(String url, String table) throws Exception { try(var c=DriverManager.getConnection(url);var st=c.createStatement();var rs=st.executeQuery("SELECT count(*) FROM banking."+table)){assertTrue(rs.next());return rs.getInt(1);}}
    private static int count(String url, String table, UUID id) throws Exception { try(var c=DriverManager.getConnection(url);var ps=c.prepareStatement("SELECT count(*) FROM banking."+table+" WHERE id=?")){ps.setObject(1,id);try(var rs=ps.executeQuery()){assertTrue(rs.next());return rs.getInt(1);}} }
    private static BigDecimal bd(String value) { return new BigDecimal(value); }
}
