package com.foundgine.providers.security.postgres;

import java.sql.Connection;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.Objects;
import javax.sql.DataSource;

/** Explicit, idempotent startup migration. Run before accepting warrant-authorized traffic. */
public final class PostgresSecurityWarrantRevocationEpochAnchorMigrator {
    private static final long LOCK_ID = 7_431_902_611L;
    private PostgresSecurityWarrantRevocationEpochAnchorMigrator() {}

    public static void apply(DataSource dataSource) {
        Objects.requireNonNull(dataSource, "dataSource");
        try (Connection connection = dataSource.getConnection()) {
            boolean autoCommit = connection.getAutoCommit(); connection.setAutoCommit(false);
            try (Statement statement = connection.createStatement()) {
                statement.execute("SELECT pg_advisory_xact_lock(" + LOCK_ID + ")");
                statement.execute("CREATE TABLE IF NOT EXISTS " + PostgresSecurityWarrantRevocationEpochAnchor.TABLE + " ("
                        + "singleton_id smallint PRIMARY KEY CHECK (singleton_id = 1), "
                        + "high_water_mark bigint NOT NULL CHECK (high_water_mark >= 0))");
                statement.execute("INSERT INTO " + PostgresSecurityWarrantRevocationEpochAnchor.TABLE
                        + " (singleton_id, high_water_mark) VALUES (1, 0) ON CONFLICT (singleton_id) DO NOTHING");
                connection.commit();
            } catch (SQLException | RuntimeException e) { connection.rollback(); throw e; }
            finally { connection.setAutoCommit(autoCommit); }
        } catch (SQLException e) { throw new IllegalStateException("Could not migrate PostgreSQL warrant revocation anchor.", e); }
    }
}
