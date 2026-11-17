package com.foundgine.providers.security.postgres;

import com.foundgine.core.semantic.security.warrants.ISecurityWarrantRevocationEpochAnchor;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.Objects;
import javax.sql.DataSource;

/** PostgreSQL primary-backed durable high-water mark. The supplied DataSource must not route reads to replicas. */
public final class PostgresSecurityWarrantRevocationEpochAnchor implements ISecurityWarrantRevocationEpochAnchor {
    public static final String TABLE = "foundgine_warrant_revocation_epoch";
    private final DataSource dataSource;

    public PostgresSecurityWarrantRevocationEpochAnchor(DataSource dataSource) {
        this.dataSource = Objects.requireNonNull(dataSource, "dataSource");
    }

    @Override public long readHighWaterMark() {
        String sql = "SELECT pg_is_in_recovery(), high_water_mark FROM " + TABLE + " WHERE singleton_id = 1";
        try (Connection connection = dataSource.getConnection(); Statement statement = connection.createStatement(); ResultSet rs = statement.executeQuery(sql)) {
            if (!rs.next()) throw new IllegalStateException("PostgreSQL warrant revocation anchor row is missing; failing closed.");
            if (rs.getBoolean(1)) throw new IllegalStateException("PostgreSQL warrant revocation anchor is connected to a standby; failing closed.");
            long value = rs.getLong(2);
            if (value < 0 || rs.next()) throw new IllegalStateException("PostgreSQL warrant revocation anchor is invalid; failing closed.");
            return value;
        } catch (SQLException e) { throw new IllegalStateException("PostgreSQL warrant revocation anchor unavailable; failing closed.", e); }
    }

    @Override public void advanceHighWaterMark(long sequence) {
        if (sequence < 0) throw new IllegalArgumentException("sequence must be non-negative.");
        String sql = "INSERT INTO " + TABLE + " (singleton_id, high_water_mark) VALUES (1, ?) "
                + "ON CONFLICT (singleton_id) DO UPDATE SET high_water_mark = GREATEST(" + TABLE + ".high_water_mark, EXCLUDED.high_water_mark)";
        try (Connection connection = dataSource.getConnection()) {
            boolean autoCommit = connection.getAutoCommit();
            connection.setAutoCommit(false);
            try (Statement durability = connection.createStatement()) {
                durability.execute("SET LOCAL synchronous_commit = on");
                try (PreparedStatement statement = connection.prepareStatement(sql)) {
                    statement.setLong(1, sequence);
                    if (statement.executeUpdate() != 1) throw new SQLException("Expected exactly one anchor row update.");
                }
                connection.commit();
            } catch (SQLException | RuntimeException e) {
                connection.rollback(); throw e;
            } finally { connection.setAutoCommit(autoCommit); }
        } catch (SQLException e) { throw new IllegalStateException("PostgreSQL warrant revocation anchor could not durably advance; failing closed.", e); }
    }
}
