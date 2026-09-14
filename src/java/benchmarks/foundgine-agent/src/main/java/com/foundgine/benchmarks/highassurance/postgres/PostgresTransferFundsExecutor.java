package com.foundgine.benchmarks.highassurance.postgres;

import com.foundgine.benchmarks.highassurance.TransferFunds;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;

import java.math.BigDecimal;
import java.sql.*;
import java.time.Instant;
import java.util.*;
import java.util.function.BiConsumer;
import java.util.function.Function;

/**
 * JDBC/PostgreSQL execution adapter for high-assurance TransferFunds.
 * The adapter owns the physical transaction and never treats a semantic plan as
 * sufficient authority: authorization is loaded, locked, bound to the request,
 * re-evaluated after mutation and checked again immediately before commit.
 */
public final class PostgresTransferFundsExecutor {
    public enum FaultPoint { AFTER_MUTATION_BEFORE_COMMIT, BEFORE_AUTHORIZATION_COMMIT_CHECK }

    public record Result(UUID transferId, BigDecimal sourceBalance, BigDecimal destinationBalance,
                         boolean replay, long authorizationVersion, String authorizationFingerprint) {}

    public record Account(UUID id, int tenantId, UUID ownerId, BigDecimal balance,
                          BigDecimal pendingTransactions, BigDecimal regulatoryHold,
                          BigDecimal dailyTransferred, BigDecimal dailyLimit, boolean frozen) {}

    public record AuthorizationContext(UUID actorId, int tenantId, boolean allowed, long version, String fingerprint) {}

    @FunctionalInterface
    public interface Authorization {
        PostgresAuthorizationDecision decide(UUID actorId, Account source, Account destination);
    }

    private record IdempotencyRow(UUID actorId, int tenantId, UUID source, UUID destination,
                                  BigDecimal amount, UUID transferId, BigDecimal sourceBalance,
                                  BigDecimal destinationBalance) {}

    private final String jdbcUrl;
    private final Properties properties;
    private final Authorization authorization;
    private final BiConsumer<FaultPoint, Connection> faultInjector;

    public PostgresTransferFundsExecutor(String jdbcUrl, Properties properties, Authorization authorization) {
        this(jdbcUrl, properties, authorization, (point, connection) -> {});
    }

    public PostgresTransferFundsExecutor(String jdbcUrl, Properties properties,
                                         Authorization authorization,
                                         BiConsumer<FaultPoint, Connection> faultInjector) {
        this.jdbcUrl = Objects.requireNonNull(jdbcUrl);
        this.properties = properties == null ? new Properties() : copy(properties);
        this.authorization = Objects.requireNonNull(authorization);
        this.faultInjector = Objects.requireNonNull(faultInjector);
    }

    public Result execute(UUID actorId, int tenantId, TransferFunds.Command command) throws SQLException {
        Objects.requireNonNull(actorId); validateShape(command);
        try (Connection connection = DriverManager.getConnection(jdbcUrl, properties)) {
            connection.setAutoCommit(false);
            connection.setTransactionIsolation(Connection.TRANSACTION_READ_COMMITTED);
            try {
                advisoryLock(connection, command.idempotencyKey());
                var existing = findIdempotency(connection, command.idempotencyKey());
                if (existing != null) {
                    ensureReplayMatches(existing, actorId, tenantId, command);
                    connection.commit();
                    return new Result(existing.transferId, existing.sourceBalance, existing.destinationBalance,
                            true, 0, "replay");
                }

                var accounts = loadAccountsForUpdate(connection, command.sourceAccountId(), command.destinationAccountId());
                var source = requireAccount(accounts, command.sourceAccountId());
                var destination = requireAccount(accounts, command.destinationAccountId());
                validateExecution(actorId, tenantId, source, destination, command.amount());

                var context = loadAuthorizationContextForUpdate(connection, actorId, tenantId);
                var decision = authorization.decide(actorId, source, destination);
                ensureAuthorized(decision);
                ensureAuthorizationMatchesContext(decision, context);
                var binding = AuthorizationExecutionBinding.create(actorId, tenantId, command, decision);

                var transferId = UUID.randomUUID();
                var sourceBalance = source.balance().subtract(command.amount());
                var destinationBalance = destination.balance().add(command.amount());
                applyMutation(connection, actorId, tenantId, command, transferId, sourceBalance, destinationBalance);
                faultInjector.accept(FaultPoint.AFTER_MUTATION_BEFORE_COMMIT, connection);
                faultInjector.accept(FaultPoint.BEFORE_AUTHORIZATION_COMMIT_CHECK, connection);

                var commitDecision = authorization.decide(actorId, source, destination);
                ensureAuthorizationMatches(decision, commitDecision);
                binding.validateAgainst(actorId, tenantId, command, commitDecision);
                var currentContext = loadAuthorizationContextForUpdate(connection, actorId, tenantId);
                ensureAuthorizationMatchesContext(decision, currentContext);

                connection.commit();
                return new Result(transferId, sourceBalance, destinationBalance, false,
                        decision.version(), decision.fingerprint());
            } catch (Throwable failure) {
                try { connection.rollback(); } catch (SQLException ignored) { failure.addSuppressed(ignored); }
                if (failure instanceof SQLException sql) throw sql;
                if (failure instanceof RuntimeException runtime) throw runtime;
                throw new IllegalStateException("Transfer execution failed.", failure);
            }
        }
    }

    private static Properties copy(Properties source) {
        var copy = new Properties();
        copy.putAll(source);
        return copy;
    }

    private static void validateShape(TransferFunds.Command command) {
        Objects.requireNonNull(command);
        if (command.amount() == null || command.amount().signum() <= 0) throw new IllegalArgumentException("Transfer amount must be greater than zero.");
        if (command.idempotencyKey() == null || command.idempotencyKey().isBlank()) throw new IllegalArgumentException("Idempotency key is required.");
        if (command.sourceAccountId().equals(command.destinationAccountId())) throw new IllegalArgumentException("Source and destination accounts must differ.");
    }

    private static void validateExecution(UUID actorId, int tenantId, Account source, Account destination, BigDecimal amount) {
        if (source.tenantId() != tenantId || destination.tenantId() != tenantId) throw new IllegalStateException("Tenant boundary violation.");
        if (!source.ownerId().equals(actorId) || !destination.ownerId().equals(actorId)) throw new SemanticAuthorizationException("Transfer capability requires ownership of both accounts.");
        if (source.frozen() || destination.frozen()) throw new IllegalStateException("Frozen accounts cannot participate in transfers.");
        if (source.dailyTransferred().add(amount).compareTo(source.dailyLimit()) > 0) throw new IllegalStateException("Transfer exceeds the source account daily limit.");
        var available = source.balance().subtract(source.pendingTransactions()).subtract(source.regulatoryHold());
        if (available.compareTo(amount) < 0) throw new IllegalStateException("Insufficient available funds.");
    }

    private static void ensureAuthorized(PostgresAuthorizationDecision decision) {
        Objects.requireNonNull(decision);
        if (!decision.allowed()) throw new SemanticAuthorizationException("Transfer capability is not authorized for this actor and account pair.");
    }

    private static void ensureAuthorizationMatches(PostgresAuthorizationDecision expected, PostgresAuthorizationDecision current) {
        ensureAuthorized(current);
        if (expected.version() != current.version() || !expected.fingerprint().equals(current.fingerprint()))
            throw new IllegalStateException("Authorization context changed during transfer execution; the authorization evidence is stale.");
    }

    private static void ensureAuthorizationMatchesContext(PostgresAuthorizationDecision decision, AuthorizationContext context) {
        if (context == null) throw new IllegalStateException("Authoritative authorization context is missing; authorization fails closed.");
        if (!context.allowed()) throw new SemanticAuthorizationException("Authoritative authorization context is not allowed.");
        if (decision.version() != context.version() || !decision.fingerprint().equals(context.fingerprint()))
            throw new IllegalStateException("Authorization decision does not match authoritative PostgreSQL authorization context.");
    }

    private static Map<UUID, Account> loadAccountsForUpdate(Connection c, UUID sourceId, UUID destinationId) throws SQLException {
        var sql = "SELECT id,tenant_id,owner_id,balance,pending_transactions,regulatory_hold,daily_transferred,daily_limit,is_frozen " +
                "FROM banking.bank_account WHERE id IN (?,?) ORDER BY id FOR UPDATE";
        var result = new HashMap<UUID, Account>();
        try (var ps = c.prepareStatement(sql)) {
            ps.setObject(1, sourceId); ps.setObject(2, destinationId);
            try (var rs = ps.executeQuery()) {
                while (rs.next()) result.put(rs.getObject(1, UUID.class), account(rs));
            }
        }
        return result;
    }

    private static Account account(ResultSet rs) throws SQLException {
        return new Account(rs.getObject(1, UUID.class), rs.getInt(2), rs.getObject(3, UUID.class),
                rs.getBigDecimal(4), rs.getBigDecimal(5), rs.getBigDecimal(6), rs.getBigDecimal(7),
                rs.getBigDecimal(8), rs.getBoolean(9));
    }

    private static Account requireAccount(Map<UUID, Account> accounts, UUID id) {
        var account = accounts.get(id);
        if (account == null) throw new IllegalStateException("Account '" + id + "' was not found.");
        return account;
    }

    private static void advisoryLock(Connection c, String key) throws SQLException {
        try (var ps = c.prepareStatement("SELECT pg_advisory_xact_lock(hashtextextended(?,0))")) {
            ps.setString(1, key); ps.execute();
        }
    }

    private static IdempotencyRow findIdempotency(Connection c, String key) throws SQLException {
        var sql = "SELECT actor_id,tenant_id,source_account_id,destination_account_id,amount,transfer_id,source_balance,destination_balance " +
                "FROM banking.transfer_idempotency WHERE idempotency_key=?";
        try (var ps = c.prepareStatement(sql)) {
            ps.setString(1, key);
            try (var rs = ps.executeQuery()) {
                if (!rs.next()) return null;
                return new IdempotencyRow(rs.getObject(1, UUID.class), rs.getInt(2), rs.getObject(3, UUID.class),
                        rs.getObject(4, UUID.class), rs.getBigDecimal(5), rs.getObject(6, UUID.class),
                        rs.getBigDecimal(7), rs.getBigDecimal(8));
            }
        }
    }

    private static AuthorizationContext loadAuthorizationContextForUpdate(Connection c, UUID actorId, int tenantId) throws SQLException {
        var sql = "SELECT actor_id,tenant_id,allowed,version,fingerprint FROM banking.authorization_context WHERE actor_id=? AND tenant_id=? FOR UPDATE";
        try (var ps = c.prepareStatement(sql)) {
            ps.setObject(1, actorId); ps.setInt(2, tenantId);
            try (var rs = ps.executeQuery()) {
                if (!rs.next()) return null;
                return new AuthorizationContext(rs.getObject(1, UUID.class), rs.getInt(2), rs.getBoolean(3), rs.getLong(4), rs.getString(5));
            }
        }
    }

    private static void applyMutation(Connection c, UUID actorId, int tenantId, TransferFunds.Command command,
                                      UUID transferId, BigDecimal sourceBalance, BigDecimal destinationBalance) throws SQLException {
        var sql = """
                WITH source_update AS (
                    UPDATE banking.bank_account SET balance=balance-?, daily_transferred=daily_transferred+? WHERE id=? RETURNING id
                ), destination_update AS (
                    UPDATE banking.bank_account SET balance=balance+? WHERE id=? RETURNING id
                ), idempotency_insert AS (
                    INSERT INTO banking.transfer_idempotency
                    (idempotency_key,actor_id,tenant_id,source_account_id,destination_account_id,amount,transfer_id,source_balance,destination_balance)
                    SELECT ?,?,?,?,?,?,?,?,? FROM source_update CROSS JOIN destination_update RETURNING transfer_id
                ), audit_insert AS (
                    INSERT INTO banking.transfer_audit
                    (transfer_id,action,actor_id,tenant_id,source_account_id,destination_account_id,amount)
                    SELECT transfer_id,'transferFunds',?,?,?,?,? FROM idempotency_insert
                ) SELECT transfer_id FROM idempotency_insert
                """;
        try (var ps = c.prepareStatement(sql)) {
            int i=1;
            ps.setBigDecimal(i++, command.amount()); ps.setBigDecimal(i++, command.amount()); ps.setObject(i++, command.sourceAccountId());
            ps.setBigDecimal(i++, command.amount()); ps.setObject(i++, command.destinationAccountId());
            ps.setString(i++, command.idempotencyKey()); ps.setObject(i++, actorId); ps.setInt(i++, tenantId);
            ps.setObject(i++, command.sourceAccountId()); ps.setObject(i++, command.destinationAccountId()); ps.setBigDecimal(i++, command.amount());
            ps.setObject(i++, transferId); ps.setBigDecimal(i++, sourceBalance); ps.setBigDecimal(i++, destinationBalance);
            ps.setObject(i++, actorId); ps.setInt(i++, tenantId); ps.setObject(i++, command.sourceAccountId());
            ps.setObject(i++, command.destinationAccountId()); ps.setBigDecimal(i++, command.amount());
            try (var rs = ps.executeQuery()) {
                if (!rs.next() || !transferId.equals(rs.getObject(1, UUID.class))) throw new IllegalStateException("PostgreSQL mutation did not return expected transfer identity.");
            }
        }
    }

    private static void ensureReplayMatches(IdempotencyRow row, UUID actorId, int tenantId, TransferFunds.Command c) {
        if (!row.actorId().equals(actorId) || row.tenantId()!=tenantId || !row.source().equals(c.sourceAccountId()) ||
                !row.destination().equals(c.destinationAccountId()) || row.amount().compareTo(c.amount())!=0)
            throw new IllegalStateException("The idempotency key is already bound to a different transfer request.");
    }


}
