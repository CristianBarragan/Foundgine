package com.foundgine.benchmarks.highassurance.postgres;

import com.foundgine.runtime.controlplane.AuthorizationContextIntegrityKeyRing;
import com.foundgine.runtime.controlplane.AuthorizationContextIntegrityKeyRingManager;

import java.sql.*;
import java.util.HashSet;
import java.util.Objects;
import java.util.Set;
import java.util.UUID;

/**
 * PostgreSQL-backed authorization-context lifecycle boundary.
 *
 * <p>This is the Java parity port of the C# high-assurance store. Integrity is verified before
 * authorization evidence is trusted; lifecycle writes require registered writer provenance and a
 * strictly increasing writer sequence.
 */
public final class PostgresAuthorizationContextStore {
    public record AuthorizationContextRow(
            UUID actorId,
            int tenantId,
            boolean allowed,
            long version,
            String fingerprint,
            String integrityAlgorithm,
            String integrityKeyId,
            String integrityTag) {}

    public record AuthorizationWriteProvenance(
            UUID writerId,
            UUID actorId,
            int tenantId,
            long writeSequence,
            String credentialFingerprint) {}

    private final AuthorizationContextIntegrityKeyRingManager integrityManager;

    public PostgresAuthorizationContextStore(AuthorizationContextIntegrityKeyRing integrity) {
        this(new AuthorizationContextIntegrityKeyRingManager(Objects.requireNonNull(integrity)));
    }

    public PostgresAuthorizationContextStore(
            AuthorizationContextIntegrityKeyRingManager integrityManager) {
        this.integrityManager = Objects.requireNonNull(integrityManager);
    }

    public AuthorizationContextIntegrityKeyRing currentIntegrityKeyRing() {
        return integrityManager.snapshot();
    }

    public Set<String> getPersistedIntegrityKeyIds(Connection connection, boolean transactional)
            throws SQLException {
        var result = new HashSet<String>();
        try (var command =
                connection.prepareStatement(
                        "SELECT integrity_key_id FROM banking.authorization_context UNION SELECT"
                            + " integrity_key_id FROM banking.authorization_context_tombstone")) {
            try (var reader = command.executeQuery()) {
                while (reader.next()) result.add(reader.getString(1));
            }
        }
        return Set.copyOf(result);
    }

    public AuthorizationContextRow loadForUpdate(Connection connection, UUID actorId, int tenantId)
            throws SQLException {
        String sql =
                "SELECT actor_id, tenant_id, allowed, version, fingerprint, integrity_algorithm,"
                    + " integrity_key_id, integrity_tag FROM banking.authorization_context WHERE"
                    + " actor_id=? AND tenant_id=? FOR UPDATE";
        try (var command = connection.prepareStatement(sql)) {
            command.setObject(1, actorId);
            command.setInt(2, tenantId);
            try (var reader = command.executeQuery()) {
                if (!reader.next()) return null;
                var row =
                        new AuthorizationContextRow(
                                reader.getObject(1, UUID.class),
                                reader.getInt(2),
                                reader.getBoolean(3),
                                reader.getLong(4),
                                reader.getString(5),
                                reader.getString(6),
                                reader.getString(7),
                                reader.getString(8));
                if (!integrityManager
                        .snapshot()
                        .verifyContextTag(
                                row.actorId(),
                                row.tenantId(),
                                row.allowed(),
                                row.version(),
                                row.fingerprint(),
                                row.integrityAlgorithm(),
                                row.integrityKeyId(),
                                row.integrityTag())) {
                    throw new IllegalStateException(
                            "Authorization context integrity verification failed; authorization"
                                    + " fails closed.");
                }
                return row;
            }
        }
    }

    public void create(
            Connection connection,
            UUID actorId,
            int tenantId,
            boolean allowed,
            long version,
            String fingerprint,
            AuthorizationWriteProvenance provenance)
            throws SQLException {
        validate(version, fingerprint);
        validateWriter(connection, provenance, actorId, tenantId);
        verifyTombstoneAllowsCreate(connection, actorId, tenantId, version);

        var ring = integrityManager.snapshot();
        String sql =
                "INSERT INTO banking.authorization_context"
                    + " (actor_id,tenant_id,allowed,version,fingerprint,integrity_algorithm,integrity_key_id,integrity_tag)"
                    + " VALUES (?,?,?,?,?,?,?,?)";
        try (var command = connection.prepareStatement(sql)) {
            bind(command, actorId, tenantId, allowed, version, fingerprint);
            command.setString(6, AuthorizationContextIntegrityKeyRing.currentAlgorithmVersion());
            command.setString(7, ring.activeKeyId());
            command.setString(
                    8, ring.computeContextTag(actorId, tenantId, allowed, version, fingerprint));
            if (command.executeUpdate() != 1)
                throw new IllegalStateException(
                        "Authorization context create did not affect exactly one row.");
        }
    }

    public void update(
            Connection connection,
            UUID actorId,
            int tenantId,
            boolean allowed,
            long version,
            String fingerprint,
            AuthorizationWriteProvenance provenance)
            throws SQLException {
        validate(version, fingerprint);
        validateWriter(connection, provenance, actorId, tenantId);
        var current = loadForUpdate(connection, actorId, tenantId);
        if (current == null)
            throw new IllegalStateException("Authorization context does not exist.");
        if (version <= current.version())
            throw new IllegalStateException(
                    "Authorization context version must increase monotonically.");

        var ring = integrityManager.snapshot();
        String sql =
                "UPDATE banking.authorization_context SET"
                    + " allowed=?,version=?,fingerprint=?,integrity_algorithm=?,integrity_key_id=?,integrity_tag=?"
                    + " WHERE actor_id=? AND tenant_id=?";
        try (var command = connection.prepareStatement(sql)) {
            command.setBoolean(1, allowed);
            command.setLong(2, version);
            command.setString(3, fingerprint);
            command.setString(4, AuthorizationContextIntegrityKeyRing.currentAlgorithmVersion());
            command.setString(5, ring.activeKeyId());
            command.setString(
                    6, ring.computeContextTag(actorId, tenantId, allowed, version, fingerprint));
            command.setObject(7, actorId);
            command.setInt(8, tenantId);
            if (command.executeUpdate() != 1)
                throw new IllegalStateException(
                        "Authorization context update did not affect exactly one row.");
        }
    }

    public void revoke(
            Connection connection,
            UUID actorId,
            int tenantId,
            long version,
            String fingerprint,
            AuthorizationWriteProvenance provenance)
            throws SQLException {
        update(connection, actorId, tenantId, false, version, fingerprint, provenance);
    }

    public void delete(
            Connection connection,
            UUID actorId,
            int tenantId,
            AuthorizationWriteProvenance provenance)
            throws SQLException {
        validateWriter(connection, provenance, actorId, tenantId);
        var current = loadForUpdate(connection, actorId, tenantId);
        if (current == null) return;

        var ring = integrityManager.snapshot();
        String tombstone =
                "INSERT INTO banking.authorization_context_tombstone"
                    + " (actor_id,tenant_id,last_version,last_fingerprint,integrity_algorithm,integrity_key_id,integrity_tag)"
                    + " VALUES (?,?,?,?,?,?,?) ON CONFLICT (actor_id,tenant_id) DO UPDATE SET "
                    + "last_version=EXCLUDED.last_version,last_fingerprint=EXCLUDED.last_fingerprint,integrity_algorithm=EXCLUDED.integrity_algorithm,integrity_key_id=EXCLUDED.integrity_key_id,integrity_tag=EXCLUDED.integrity_tag";
        try (var command = connection.prepareStatement(tombstone)) {
            command.setObject(1, actorId);
            command.setInt(2, tenantId);
            command.setLong(3, current.version());
            command.setString(4, current.fingerprint());
            command.setString(5, AuthorizationContextIntegrityKeyRing.currentAlgorithmVersion());
            command.setString(6, ring.activeKeyId());
            command.setString(
                    7,
                    ring.computeTombstoneTag(
                            actorId, tenantId, current.version(), current.fingerprint()));
            command.executeUpdate();
        }
        try (var command =
                connection.prepareStatement(
                        "DELETE FROM banking.authorization_context WHERE actor_id=? AND"
                                + " tenant_id=?")) {
            command.setObject(1, actorId);
            command.setInt(2, tenantId);
            if (command.executeUpdate() != 1)
                throw new IllegalStateException(
                        "Authorization context deletion did not affect exactly one row.");
        }
    }

    private void verifyTombstoneAllowsCreate(
            Connection connection, UUID actorId, int tenantId, long version) throws SQLException {
        String sql =
                "SELECT"
                    + " last_version,last_fingerprint,integrity_algorithm,integrity_key_id,integrity_tag"
                    + " FROM banking.authorization_context_tombstone WHERE actor_id=? AND"
                    + " tenant_id=? FOR UPDATE";
        try (var command = connection.prepareStatement(sql)) {
            command.setObject(1, actorId);
            command.setInt(2, tenantId);
            try (var reader = command.executeQuery()) {
                if (!reader.next()) return;
                var lastVersion = reader.getLong(1);
                var ring = integrityManager.snapshot();
                if (!ring.verifyTombstoneTag(
                        actorId,
                        tenantId,
                        lastVersion,
                        reader.getString(2),
                        reader.getString(3),
                        reader.getString(4),
                        reader.getString(5)))
                    throw new IllegalStateException(
                            "Authorization lifecycle tombstone integrity verification failed;"
                                    + " authorization fails closed.");
                if (version <= lastVersion)
                    throw new IllegalStateException(
                            "Authorization context version must exceed the last committed lifecycle"
                                    + " version.");
            }
        }
    }

    private static void validate(long version, String fingerprint) {
        if (version <= 0)
            throw new IllegalArgumentException("Authorization context versions must be positive.");
        if (fingerprint == null || fingerprint.isBlank())
            throw new IllegalArgumentException(
                    "Authorization context fingerprint cannot be empty.");
    }

    private static void validateWriter(
            Connection connection, AuthorizationWriteProvenance p, UUID actorId, int tenantId)
            throws SQLException {
        if (p == null || p.writerId() == null || p.writerId().equals(new UUID(0, 0)))
            throw new SecurityException(
                    "Authorization context writes require registered writer provenance.");
        if (!actorId.equals(p.actorId()) || tenantId != p.tenantId())
            throw new SecurityException(
                    "Authorization writer is not scoped to the target authorization identity.");
        if (p.writeSequence() <= 0)
            throw new IllegalArgumentException("Writer sequence must be positive.");
        if (p.credentialFingerprint() == null || p.credentialFingerprint().isBlank())
            throw new IllegalArgumentException("Writer credential fingerprint is required.");

        String sql =
                "SELECT"
                    + " tenant_id,actor_id,active,database_role,credential_fingerprint,last_write_sequence"
                    + " FROM banking.authorization_context_writer WHERE writer_id=? FOR UPDATE";
        try (var command = connection.prepareStatement(sql)) {
            command.setObject(1, p.writerId());
            try (var reader = command.executeQuery()) {
                if (!reader.next())
                    throw new SecurityException("Authorization writer is not registered.");
                if (!reader.getBoolean(3)
                        || reader.getInt(1) != tenantId
                        || !actorId.equals(reader.getObject(2, UUID.class)))
                    throw new SecurityException(
                            "Authorization writer is inactive or outside its tenant/actor scope.");
                if (!reader.getString(5).equals(p.credentialFingerprint()))
                    throw new SecurityException(
                            "Authorization writer credential provenance does not match the"
                                    + " registered credential.");
                if (!reader.getString(4).equals(currentDatabaseRole(connection)))
                    throw new SecurityException(
                            "Authorization writer database-role provenance does not match the"
                                    + " current PostgreSQL session.");
                if (p.writeSequence() <= reader.getLong(6))
                    throw new IllegalStateException("Authorization writer sequence is stale.");
            }
        }
        try (var command =
                connection.prepareStatement(
                        "UPDATE banking.authorization_context_writer SET last_write_sequence=?"
                                + " WHERE writer_id=?")) {
            command.setLong(1, p.writeSequence());
            command.setObject(2, p.writerId());
            if (command.executeUpdate() != 1)
                throw new IllegalStateException(
                        "Authorization writer sequence update did not affect exactly one row.");
        }
    }

    private static String currentDatabaseRole(Connection connection) throws SQLException {
        try (var command = connection.prepareStatement("SELECT current_user");
                var reader = command.executeQuery()) {
            if (!reader.next())
                throw new IllegalStateException("Unable to resolve PostgreSQL current_user.");
            return reader.getString(1);
        }
    }

    private static void bind(
            PreparedStatement command,
            UUID actorId,
            int tenantId,
            boolean allowed,
            long version,
            String fingerprint)
            throws SQLException {
        command.setObject(1, actorId);
        command.setInt(2, tenantId);
        command.setBoolean(3, allowed);
        command.setLong(4, version);
        command.setString(5, fingerprint);
    }
}
