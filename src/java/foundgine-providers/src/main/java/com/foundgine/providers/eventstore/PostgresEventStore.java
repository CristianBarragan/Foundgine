package com.foundgine.providers.eventstore;

import com.foundgine.providers.gateway.FoundgineProviderCapability;
import com.foundgine.providers.gateway.FoundgineProviderKind;
import com.foundgine.providers.gateway.IFoundgineProvider;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import javax.sql.DataSource;

/** PostgreSQL event store over plain JDBC. Sequence allocation is serialized per stream with advisory locks. */
public final class PostgresEventStore implements IEventStore, IFoundgineProvider {
    private final DataSource dataSource;
    private final EventStoreOptions options;

    public PostgresEventStore(DataSource dataSource) { this(dataSource, null); }

    public PostgresEventStore(DataSource dataSource, EventStoreOptions options) {
        if (dataSource == null) throw new IllegalArgumentException("dataSource is required");
        this.dataSource = dataSource;
        this.options = options == null ? new EventStoreOptions() : options;
        this.options.validate();
    }

    @Override public String id() { return "foundgine.postgres.eventstore"; }
    @Override public String name() { return "PostgreSQL event store"; }
    @Override public String version() { return "1.0.0"; }
    @Override public FoundgineProviderKind kind() { return FoundgineProviderKind.EVENT_STORE; }
    @Override public long capabilities() {
        return FoundgineProviderCapability.mask(FoundgineProviderCapability.DURABLE_STORAGE,
                FoundgineProviderCapability.ORDERED_EVENTS, FoundgineProviderCapability.EVENT_REPLAY,
                FoundgineProviderCapability.TRANSACTIONAL_APPEND, FoundgineProviderCapability.TENANT_ISOLATION);
    }

    @Override
    public <T> long append(String tenantId, String streamId, UUID eventId, String eventType, Instant occurredAt,
            T payload, PayloadCodec<T> codec, String correlationId) {
        validateIdentity(tenantId, "tenantId");
        validateIdentity(streamId, "streamId");
        if (correlationId != null) validateIdentity(correlationId, "correlationId");
        validateIdentity(eventType, "eventType");
        if (eventId == null || eventId.getMostSignificantBits() == 0 && eventId.getLeastSignificantBits() == 0)
            throw new IllegalArgumentException("Event ID must be stable and non-empty.");
        if (occurredAt == null) throw new IllegalArgumentException("occurredAt is required");
        if (codec == null) throw new IllegalArgumentException("codec is required");
        String json = codec.toJson(payload);

        try (Connection c = dataSource.getConnection()) {
            boolean autoCommit = c.getAutoCommit();
            c.setAutoCommit(false);
            try {
                // A per-event advisory lock makes duplicate checks safe across app instances.
                advisoryLock(c, "event:" + tenantId + ":" + eventId.toString().replace("-", ""));
                try (PreparedStatement ps = c.prepareStatement(
                        "SELECT sequence FROM foundgine_events WHERE tenant_id = ? AND event_id = ?")) {
                    ps.setString(1, tenantId);
                    ps.setObject(2, eventId);
                    try (ResultSet rs = ps.executeQuery()) {
                        if (rs.next()) { long existing = rs.getLong(1); c.commit(); return existing; }
                    }
                }
                // Serialize sequence allocation for this tenant/stream.
                advisoryLock(c, "stream:" + tenantId + ":" + streamId);
                long sequence;
                try (PreparedStatement ps = c.prepareStatement("""
                        INSERT INTO foundgine_event_streams (tenant_id, stream_id, last_sequence)
                        VALUES (?, ?, 1)
                        ON CONFLICT (tenant_id, stream_id)
                        DO UPDATE SET last_sequence = foundgine_event_streams.last_sequence + 1
                        RETURNING last_sequence""")) {
                    ps.setString(1, tenantId);
                    ps.setString(2, streamId);
                    try (ResultSet rs = ps.executeQuery()) { rs.next(); sequence = rs.getLong(1); }
                }
                try (PreparedStatement ps = c.prepareStatement("""
                        INSERT INTO foundgine_events
                            (tenant_id, stream_id, sequence, event_id, event_type, occurred_at, payload, correlation_id)
                        VALUES (?, ?, ?, ?, ?, ?, ?::jsonb, ?)""")) {
                    ps.setString(1, tenantId);
                    ps.setString(2, streamId);
                    ps.setLong(3, sequence);
                    ps.setObject(4, eventId);
                    ps.setString(5, eventType);
                    ps.setObject(6, OffsetDateTime.ofInstant(occurredAt, ZoneOffset.UTC));
                    ps.setString(7, json);
                    ps.setString(8, correlationId);
                    ps.executeUpdate();
                }
                c.commit();
                return sequence;
            } catch (SQLException | RuntimeException e) {
                try { c.rollback(); } catch (SQLException suppressed) { e.addSuppressed(suppressed); }
                throw e;
            } finally {
                try { c.setAutoCommit(autoCommit); } catch (SQLException ignored) { }
            }
        } catch (SQLException e) {
            throw new EventStoreException("Failed to append event.", e);
        }
    }

    @Override
    public <T> List<EventEnvelope<T>> read(String tenantId, String streamId, long afterSequence, Integer take,
            PayloadCodec<T> codec) {
        validateIdentity(tenantId, "tenantId");
        validateIdentity(streamId, "streamId");
        if (afterSequence < 0) throw new IllegalArgumentException("afterSequence must not be negative");
        if (codec == null) throw new IllegalArgumentException("codec is required");
        int limit = resolveTake(take);
        return query("""
                SELECT event_id, tenant_id, stream_id, sequence, event_type, occurred_at, payload::text, correlation_id
                FROM foundgine_events
                WHERE tenant_id = ? AND stream_id = ? AND sequence > ?
                ORDER BY sequence
                LIMIT ?""", limit, codec, ps -> {
                    ps.setString(1, tenantId); ps.setString(2, streamId); ps.setLong(3, afterSequence); ps.setInt(4, limit);
                });
    }

    @Override
    public <T> List<EventEnvelope<T>> readByCorrelation(String tenantId, String streamId, String correlationId,
            int take, PayloadCodec<T> codec) {
        validateIdentity(tenantId, "tenantId");
        validateIdentity(streamId, "streamId");
        validateIdentity(correlationId, "correlationId");
        if (codec == null) throw new IllegalArgumentException("codec is required");
        int limit = resolveTake(take);
        // Latest N matches, returned oldest-first (same semantics as the in-memory journal).
        return query("""
                SELECT event_id, tenant_id, stream_id, sequence, event_type, occurred_at, payload::text, correlation_id
                FROM (
                    SELECT * FROM foundgine_events
                    WHERE tenant_id = ? AND stream_id = ? AND correlation_id = ?
                    ORDER BY sequence DESC
                    LIMIT ?
                ) latest
                ORDER BY sequence""", limit, codec, ps -> {
                    ps.setString(1, tenantId); ps.setString(2, streamId); ps.setString(3, correlationId); ps.setInt(4, limit);
                });
    }

    @Override
    public long getLatestSequence(String tenantId, String streamId) {
        validateIdentity(tenantId, "tenantId");
        validateIdentity(streamId, "streamId");
        try (Connection c = dataSource.getConnection();
             PreparedStatement ps = c.prepareStatement(
                     "SELECT last_sequence FROM foundgine_event_streams WHERE tenant_id = ? AND stream_id = ?")) {
            ps.setString(1, tenantId);
            ps.setString(2, streamId);
            try (ResultSet rs = ps.executeQuery()) { return rs.next() ? rs.getLong(1) : 0L; }
        } catch (SQLException e) {
            throw new EventStoreException("Failed to read the stream high-water mark.", e);
        }
    }

    @Override
    public int deleteExpired(Instant olderThan, int batchSize) {
        if (olderThan == null) throw new IllegalArgumentException("olderThan is required");
        if (batchSize < 1 || batchSize > 100_000) throw new IllegalArgumentException("batchSize must be 1-100,000");
        try (Connection c = dataSource.getConnection();
             PreparedStatement ps = c.prepareStatement("""
                     WITH expired AS (
                         SELECT ctid FROM foundgine_events
                         WHERE occurred_at < ?
                         ORDER BY occurred_at
                         LIMIT ?
                         FOR UPDATE SKIP LOCKED
                     )
                     DELETE FROM foundgine_events e USING expired
                     WHERE e.ctid = expired.ctid""")) {
            ps.setObject(1, OffsetDateTime.ofInstant(olderThan, ZoneOffset.UTC));
            ps.setInt(2, batchSize);
            return ps.executeUpdate();
        } catch (SQLException e) {
            throw new EventStoreException("Failed to delete expired events.", e);
        }
    }

    private int resolveTake(Integer take) {
        int limit = take == null ? options.defaultReplayBatchSize() : take;
        if (limit < 1 || limit > options.maximumReplayBatchSize())
            throw new IllegalArgumentException("take must be 1-" + options.maximumReplayBatchSize());
        return limit;
    }

    @FunctionalInterface private interface Binder { void bind(PreparedStatement ps) throws SQLException; }

    private <T> List<EventEnvelope<T>> query(String sql, int limit, PayloadCodec<T> codec, Binder binder) {
        try (Connection c = dataSource.getConnection(); PreparedStatement ps = c.prepareStatement(sql)) {
            binder.bind(ps);
            var events = new ArrayList<EventEnvelope<T>>(Math.min(limit, 256));
            try (ResultSet rs = ps.executeQuery()) {
                while (rs.next()) {
                    T payload = codec.fromJson(rs.getString(7));
                    if (payload == null) throw new IllegalStateException("Stored event payload deserialized to null.");
                    events.add(new EventEnvelope<>(rs.getObject(1, UUID.class), rs.getString(2), rs.getString(3),
                            rs.getLong(4), rs.getString(5), rs.getObject(6, OffsetDateTime.class).toInstant(),
                            payload, rs.getString(8)));
                }
            }
            return events;
        } catch (SQLException e) {
            throw new EventStoreException("Failed to read events.", e);
        }
    }

    private static void advisoryLock(Connection c, String key) throws SQLException {
        try (PreparedStatement ps = c.prepareStatement("SELECT pg_advisory_xact_lock(hashtextextended(?, 0))")) {
            ps.setString(1, key);
            ps.execute();
        }
    }

    private static void validateIdentity(String value, String name) {
        if (value == null || value.isBlank() || value.length() > 200)
            throw new IllegalArgumentException(name + " must contain 1-200 non-whitespace characters.");
    }
}
