package com.foundgine.providers.eventstore.schema;

import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import javax.sql.DataSource;

/** Applies the embedded, append-only event-store migrations. Never edit an applied migration; add a new one. */
public final class EventStoreMigrator {
    private static final String[] MIGRATIONS = {"001_initial", "002_correlation", "003_outbox", "004_gateway_approvals"};
    private static final String LOCK_KEY = "foundgine_event_store_migrations";

    private EventStoreMigrator() { }

    public static String[] migrations() { return MIGRATIONS.clone(); }

    public static void apply(DataSource dataSource) {
        if (dataSource == null) throw new IllegalArgumentException("dataSource is required");
        try (Connection c = dataSource.getConnection()) {
            // Session-level lock so concurrent hosts starting together do not race the migrations.
            try (PreparedStatement lock = c.prepareStatement("SELECT pg_advisory_lock(hashtextextended(?, 0))")) {
                lock.setString(1, LOCK_KEY);
                lock.execute();
            }
            try {
                try (Statement s = c.createStatement()) {
                    s.execute("CREATE TABLE IF NOT EXISTS foundgine_schema_migrations "
                            + "(version varchar(100) PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())");
                }
                for (String version : MIGRATIONS) applyOne(c, version);
            } finally {
                try (PreparedStatement unlock = c.prepareStatement("SELECT pg_advisory_unlock(hashtextextended(?, 0))")) {
                    unlock.setString(1, LOCK_KEY);
                    unlock.execute();
                }
            }
        } catch (SQLException e) {
            throw new IllegalStateException("Event-store migration failed.", e);
        }
    }

    private static void applyOne(Connection c, String version) throws SQLException {
        try (PreparedStatement check = c.prepareStatement(
                "SELECT EXISTS (SELECT 1 FROM foundgine_schema_migrations WHERE version = ?)")) {
            check.setString(1, version);
            try (ResultSet rs = check.executeQuery()) { if (rs.next() && rs.getBoolean(1)) return; }
        }
        String sql = load(version);
        boolean autoCommit = c.getAutoCommit();
        c.setAutoCommit(false);
        try {
            try (Statement s = c.createStatement()) { s.execute(sql); }
            try (PreparedStatement mark = c.prepareStatement(
                    "INSERT INTO foundgine_schema_migrations(version) VALUES (?) ON CONFLICT DO NOTHING")) {
                mark.setString(1, version);
                mark.executeUpdate();
            }
            c.commit();
        } catch (SQLException | RuntimeException e) {
            try { c.rollback(); } catch (SQLException suppressed) { e.addSuppressed(suppressed); }
            throw e;
        } finally {
            c.setAutoCommit(autoCommit);
        }
    }

    /** Loads an embedded migration script (also used by tests to check the scripts ship with the jar). */
    public static String load(String version) {
        String path = "/com/foundgine/providers/eventstore/schema/" + version + ".sql";
        try (InputStream in = EventStoreMigrator.class.getResourceAsStream(path)) {
            if (in == null) throw new IllegalStateException("Embedded event-store migration '" + version + "' was not found.");
            return new String(in.readAllBytes(), StandardCharsets.UTF_8);
        } catch (IOException e) {
            throw new IllegalStateException("Could not read migration '" + version + "'.", e);
        }
    }
}
