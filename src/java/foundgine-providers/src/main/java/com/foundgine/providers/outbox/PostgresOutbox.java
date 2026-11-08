package com.foundgine.providers.outbox;

import com.foundgine.providers.gateway.FoundgineProviderCapability;
import com.foundgine.providers.gateway.FoundgineProviderKind;
import com.foundgine.providers.gateway.IFoundgineProvider;
import com.foundgine.providers.json.FoundgineJson;
import java.nio.charset.StandardCharsets;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Types;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import javax.sql.DataSource;

/**
 * PostgreSQL outbox. {@link #enqueue(Connection, FoundgineOutboxMessage)} writes the message on the caller's own
 * connection, so it commits or rolls back atomically with the domain change. Leasing uses
 * {@code FOR UPDATE SKIP LOCKED}: any number of dispatcher instances may run concurrently. Ordering across messages
 * is NOT guaranteed.
 */
public final class PostgresOutbox implements IFoundgineGatewayOutbox, IFoundgineGatewayOutboxStore, IFoundgineProvider {
    private final DataSource dataSource;
    private final PostgresOutboxOptions options;
    private final Clock clock;

    public PostgresOutbox(DataSource dataSource) { this(dataSource, null, null); }

    public PostgresOutbox(DataSource dataSource, PostgresOutboxOptions options, Clock clock) {
        if (dataSource == null) throw new IllegalArgumentException("dataSource is required");
        this.dataSource = dataSource;
        this.options = options == null ? new PostgresOutboxOptions() : options;
        this.options.validate();
        this.clock = clock == null ? Clock.systemUTC() : clock;
    }

    @Override public String id() { return "foundgine.postgres.outbox"; }
    @Override public String name() { return "PostgreSQL transactional outbox"; }
    @Override public String version() { return "1.0.0"; }
    @Override public FoundgineProviderKind kind() { return FoundgineProviderKind.OUTBOX; }
    @Override public long capabilities() {
        return FoundgineProviderCapability.mask(FoundgineProviderCapability.DURABLE_STORAGE,
                FoundgineProviderCapability.TRANSACTIONAL_OUTBOX, FoundgineProviderCapability.RETRY,
                FoundgineProviderCapability.DEAD_LETTER);
    }

    /** Enqueues in its own connection. Use the connection overload for atomicity with a domain write. */
    @Override public void enqueue(FoundgineOutboxMessage message) {
        validate(message);
        try (Connection c = dataSource.getConnection()) {
            insert(c, message);
        } catch (SQLException e) {
            throw new IllegalStateException("Failed to enqueue outbox message.", e);
        }
    }

    /**
     * Enqueues on {@code connection}, which the caller owns; nothing is visible to dispatchers until the caller commits.
     * The connection must not be in auto-commit mode if atomicity with a domain write is wanted.
     */
    public void enqueue(Connection connection, FoundgineOutboxMessage message) {
        if (connection == null) throw new IllegalArgumentException("connection is required");
        validate(message);
        try {
            insert(connection, message);
        } catch (SQLException e) {
            throw new IllegalStateException("Failed to enqueue outbox message.", e);
        }
    }

    private void insert(Connection c, FoundgineOutboxMessage m) throws SQLException {
        String headers = m.headers() != null && !m.headers().isEmpty() ? FoundgineJson.stringMapToJson(m.headers()) : "{}";
        Instant createdAt = m.createdAt() != null ? m.createdAt() : clock.instant();
        try (PreparedStatement ps = c.prepareStatement("""
                INSERT INTO foundgine_outbox
                    (tenant_id, id, topic, message_key, payload, headers, correlation_id, created_at, available_at)
                VALUES (?, ?, ?, ?, ?::jsonb, ?::jsonb, ?, ?, ?)
                ON CONFLICT (tenant_id, id) DO NOTHING""")) {
            var ts = OffsetDateTime.ofInstant(createdAt, ZoneOffset.UTC);
            ps.setString(1, m.tenantId());
            ps.setObject(2, m.id());
            ps.setString(3, m.topic());
            ps.setString(4, m.key());
            ps.setString(5, m.payloadJson());
            ps.setString(6, headers);
            ps.setString(7, m.correlationId());
            ps.setObject(8, ts);
            ps.setObject(9, ts);
            ps.executeUpdate();
        }
    }

    @Override
    public List<FoundgineOutboxLease> lease(int batchSize, Duration leaseDuration, Instant now) {
        if (batchSize < 1 || batchSize > 1_000) throw new IllegalArgumentException("batchSize must be 1-1,000");
        if (leaseDuration == null || leaseDuration.isZero() || leaseDuration.isNegative())
            throw new IllegalArgumentException("leaseDuration must be positive");
        if (now == null) throw new IllegalArgumentException("now is required");
        UUID token = UUID.randomUUID();
        try (Connection c = dataSource.getConnection(); PreparedStatement ps = c.prepareStatement("""
                WITH due AS (
                    SELECT tenant_id, id FROM foundgine_outbox
                    WHERE completed_at IS NULL AND dead_lettered_at IS NULL
                      AND available_at <= ?
                      AND (locked_until IS NULL OR locked_until <= ?)
                    ORDER BY available_at, created_at
                    LIMIT ?
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE foundgine_outbox o
                SET locked_until = ?, lock_token = ?, attempts = o.attempts + 1
                FROM due
                WHERE o.tenant_id = due.tenant_id AND o.id = due.id
                RETURNING o.id, o.tenant_id, o.topic, o.message_key, o.payload::text, o.headers::text,
                          o.correlation_id, o.created_at, o.attempts""")) {
            var nowTs = OffsetDateTime.ofInstant(now, ZoneOffset.UTC);
            ps.setObject(1, nowTs);
            ps.setObject(2, nowTs);
            ps.setInt(3, batchSize);
            ps.setObject(4, OffsetDateTime.ofInstant(now.plus(leaseDuration), ZoneOffset.UTC));
            ps.setObject(5, token);
            var leases = new ArrayList<FoundgineOutboxLease>(batchSize);
            try (ResultSet rs = ps.executeQuery()) {
                while (rs.next()) {
                    var headers = FoundgineJson.parseStringMap(rs.getString(6));
                    var message = new FoundgineOutboxMessage(rs.getObject(1, UUID.class), rs.getString(2), rs.getString(3),
                            rs.getString(5), rs.getString(4), rs.getString(7), headers.isEmpty() ? null : headers,
                            rs.getObject(8, OffsetDateTime.class).toInstant());
                    leases.add(new FoundgineOutboxLease(message, rs.getInt(9), token));
                }
            }
            return leases;
        } catch (SQLException e) {
            throw new IllegalStateException("Failed to lease outbox messages.", e);
        }
    }

    @Override
    public boolean complete(FoundgineOutboxLease lease, Instant now) {
        if (lease == null) throw new IllegalArgumentException("lease is required");
        try (Connection c = dataSource.getConnection(); PreparedStatement ps = c.prepareStatement("""
                UPDATE foundgine_outbox
                SET completed_at = ?, locked_until = NULL, lock_token = NULL
                WHERE tenant_id = ? AND id = ? AND lock_token = ?
                  AND completed_at IS NULL AND dead_lettered_at IS NULL""")) {
            ps.setObject(1, OffsetDateTime.ofInstant(now, ZoneOffset.UTC));
            bindLease(ps, 2, lease);
            return ps.executeUpdate() == 1;
        } catch (SQLException e) {
            throw new IllegalStateException("Failed to complete outbox message.", e);
        }
    }

    @Override
    public boolean fail(FoundgineOutboxLease lease, String error, Instant retryAt, Instant now) {
        if (lease == null) throw new IllegalArgumentException("lease is required");
        String text = error == null ? "" : error.length() > 2000 ? error.substring(0, 2000) : error;
        String sql = retryAt == null
                ? """
                  UPDATE foundgine_outbox
                  SET dead_lettered_at = ?, locked_until = NULL, lock_token = NULL, last_error = ?
                  WHERE tenant_id = ? AND id = ? AND lock_token = ?
                    AND completed_at IS NULL AND dead_lettered_at IS NULL"""
                : """
                  UPDATE foundgine_outbox
                  SET available_at = ?, locked_until = NULL, lock_token = NULL, last_error = ?
                  WHERE tenant_id = ? AND id = ? AND lock_token = ?
                    AND completed_at IS NULL AND dead_lettered_at IS NULL""";
        try (Connection c = dataSource.getConnection(); PreparedStatement ps = c.prepareStatement(sql)) {
            ps.setObject(1, OffsetDateTime.ofInstant(retryAt == null ? now : retryAt, ZoneOffset.UTC));
            ps.setString(2, text);
            bindLease(ps, 3, lease);
            return ps.executeUpdate() == 1;
        } catch (SQLException e) {
            throw new IllegalStateException("Failed to fail outbox message.", e);
        }
    }

    /** Retention: removes completed rows older than {@code olderThan} in bounded batches. Dead letters are kept. */
    public int deleteCompleted(Instant olderThan, int batchSize) {
        if (olderThan == null) throw new IllegalArgumentException("olderThan is required");
        if (batchSize < 1 || batchSize > 100_000) throw new IllegalArgumentException("batchSize must be 1-100,000");
        try (Connection c = dataSource.getConnection(); PreparedStatement ps = c.prepareStatement("""
                WITH done AS (
                    SELECT ctid FROM foundgine_outbox
                    WHERE completed_at IS NOT NULL AND completed_at < ?
                    ORDER BY completed_at LIMIT ? FOR UPDATE SKIP LOCKED
                )
                DELETE FROM foundgine_outbox o USING done WHERE o.ctid = done.ctid""")) {
            ps.setObject(1, OffsetDateTime.ofInstant(olderThan, ZoneOffset.UTC));
            ps.setInt(2, batchSize);
            return ps.executeUpdate();
        } catch (SQLException e) {
            throw new IllegalStateException("Failed to delete completed outbox messages.", e);
        }
    }

    public int deleteCompleted(Instant olderThan) { return deleteCompleted(olderThan, 1_000); }

    private static void bindLease(PreparedStatement ps, int first, FoundgineOutboxLease lease) throws SQLException {
        ps.setString(first, lease.message().tenantId());
        ps.setObject(first + 1, lease.message().id());
        ps.setObject(first + 2, lease.leaseToken());
    }

    private void validate(FoundgineOutboxMessage m) {
        if (m == null) throw new IllegalArgumentException("message is required");
        if (m.id() == null || m.id().getMostSignificantBits() == 0 && m.id().getLeastSignificantBits() == 0)
            throw new IllegalArgumentException("Message id must be stable and non-empty.");
        identity(m.tenantId(), "tenantId");
        identity(m.topic(), "topic");
        if (m.key() != null) identity(m.key(), "key");
        if (m.correlationId() != null) identity(m.correlationId(), "correlationId");
        if (m.payloadJson() == null || m.payloadJson().isBlank()) throw new IllegalArgumentException("payloadJson is required.");
        if (m.payloadJson().getBytes(StandardCharsets.UTF_8).length > options.maxPayloadBytes())
            throw new IllegalArgumentException("payloadJson exceeds the configured maximum size.");
        if (m.headers() != null && m.headers().size() > options.maxHeaders())
            throw new IllegalArgumentException("Too many headers.");
    }

    private static void identity(String value, String name) {
        if (value == null || value.isBlank() || value.length() > 200)
            throw new IllegalArgumentException(name + " must contain 1-200 non-whitespace characters.");
    }
}
