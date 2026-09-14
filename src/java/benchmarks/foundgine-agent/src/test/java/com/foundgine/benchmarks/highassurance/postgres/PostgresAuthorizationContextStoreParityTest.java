package com.foundgine.benchmarks.highassurance.postgres;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.runtime.controlplane.AuthorizationContextIntegrityKey;
import com.foundgine.runtime.controlplane.AuthorizationContextIntegrityKeyRing;

import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.Test;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.sql.*;
import java.util.UUID;

/** PostgreSQL parity for the C# authorization-context lifecycle/provenance boundary. */
class PostgresAuthorizationContextStoreParityTest {
    private String url() {
        var value = System.getenv("FOUNDGINE_POSTGRES_CONNECTION_STRING");
        Assumptions.assumeTrue(
                value != null && !value.isBlank(),
                "FOUNDGINE_POSTGRES_CONNECTION_STRING is not configured");
        return value;
    }

    private static AuthorizationContextIntegrityKey key(String id, String material) {
        try {
            return new AuthorizationContextIntegrityKey(
                    id,
                    MessageDigest.getInstance("SHA-256")
                            .digest(("Foundgine:" + material).getBytes(StandardCharsets.UTF_8)));
        } catch (Exception e) {
            throw new AssertionError(e);
        }
    }

    private static PostgresAuthorizationContextStore store() {
        return new PostgresAuthorizationContextStore(
                new AuthorizationContextIntegrityKeyRing(key("key-v1", "store")));
    }

    @Test
    void tamperedContextFailsClosed() throws Exception {
        var url = url();
        var actor = UUID.randomUUID();
        var writer = UUID.randomUUID();
        prepare(url);
        registerWriter(url, writer, actor, 701, "writer");
        var store = store();
        try (var c = DriverManager.getConnection(url)) {
            c.setAutoCommit(false);
            store.create(c, actor, 701, true, 1, "fp-1", provenance(writer, actor, 701, 1));
            c.commit();
            try (var p =
                    c.prepareStatement(
                            "UPDATE banking.authorization_context SET allowed=false WHERE"
                                    + " actor_id=? AND tenant_id=?")) {
                p.setObject(1, actor);
                p.setInt(2, 701);
                p.executeUpdate();
            }
            c.setAutoCommit(false);
            assertThrows(IllegalStateException.class, () -> store.loadForUpdate(c, actor, 701));
            c.rollback();
        }
    }

    @Test
    void unknownKeyFailsClosed() throws Exception {
        var url = url();
        var actor = UUID.randomUUID();
        prepare(url);
        insertRawContext(url, actor, 702, true, 1, "fp-1", "unknown-key", "00".repeat(32));
        try (var c = DriverManager.getConnection(url)) {
            c.setAutoCommit(false);
            assertThrows(IllegalStateException.class, () -> store().loadForUpdate(c, actor, 702));
            c.rollback();
        }
    }

    @Test
    void versionMustIncrease() throws Exception {
        var url = url();
        var actor = UUID.randomUUID();
        var writer = UUID.randomUUID();
        prepare(url);
        registerWriter(url, writer, actor, 703, "writer");
        try (var c = DriverManager.getConnection(url)) {
            c.setAutoCommit(false);
            var s = store();
            s.create(c, actor, 703, true, 1, "fp-1", provenance(writer, actor, 703, 1));
            c.commit();
            c.setAutoCommit(false);
            assertThrows(
                    IllegalStateException.class,
                    () ->
                            s.update(
                                    c,
                                    actor,
                                    703,
                                    false,
                                    1,
                                    "fp-2",
                                    provenance(writer, actor, 703, 2)));
            c.rollback();
        }
    }

    @Test
    void crossTenantWriterIsRejected() throws Exception {
        var url = url();
        var actor = UUID.randomUUID();
        var writer = UUID.randomUUID();
        prepare(url);
        registerWriter(url, writer, actor, 704, "writer");
        try (var c = DriverManager.getConnection(url)) {
            c.setAutoCommit(false);
            assertThrows(
                    SecurityException.class,
                    () ->
                            store().create(
                                            c,
                                            actor,
                                            999,
                                            true,
                                            1,
                                            "fp",
                                            provenance(writer, actor, 999, 1)));
            c.rollback();
        }
    }

    @Test
    void actorImpersonationIsRejected() throws Exception {
        var url = url();
        var actor = UUID.randomUUID();
        var other = UUID.randomUUID();
        var writer = UUID.randomUUID();
        prepare(url);
        registerWriter(url, writer, actor, 705, "writer");
        try (var c = DriverManager.getConnection(url)) {
            c.setAutoCommit(false);
            assertThrows(
                    SecurityException.class,
                    () ->
                            store().create(
                                            c,
                                            other,
                                            705,
                                            true,
                                            1,
                                            "fp",
                                            provenance(writer, other, 705, 1)));
            c.rollback();
        }
    }

    @Test
    void inactiveWriterIsRejected() throws Exception {
        var url = url();
        var actor = UUID.randomUUID();
        var writer = UUID.randomUUID();
        prepare(url);
        registerWriter(url, writer, actor, 706, "writer");
        try (var c = DriverManager.getConnection(url)) {
            try (var p =
                    c.prepareStatement(
                            "UPDATE banking.authorization_context_writer SET active=false WHERE"
                                    + " writer_id=?")) {
                p.setObject(1, writer);
                p.executeUpdate();
            }
            c.setAutoCommit(false);
            assertThrows(
                    SecurityException.class,
                    () ->
                            store().create(
                                            c,
                                            actor,
                                            706,
                                            true,
                                            1,
                                            "fp",
                                            provenance(writer, actor, 706, 1)));
            c.rollback();
        }
    }

    @Test
    void staleWriterSequenceIsRejected() throws Exception {
        var url = url();
        var actor = UUID.randomUUID();
        var writer = UUID.randomUUID();
        prepare(url);
        registerWriter(url, writer, actor, 707, "writer");
        try (var c = DriverManager.getConnection(url)) {
            c.setAutoCommit(false);
            var s = store();
            s.create(c, actor, 707, true, 1, "fp-1", provenance(writer, actor, 707, 1));
            c.commit();
            c.setAutoCommit(false);
            assertThrows(
                    IllegalStateException.class,
                    () ->
                            s.update(
                                    c,
                                    actor,
                                    707,
                                    false,
                                    2,
                                    "fp-2",
                                    provenance(writer, actor, 707, 1)));
            c.rollback();
        }
    }

    @Test
    void deleteCreatesIntegrityProtectedTombstoneAndBlocksReplayVersion() throws Exception {
        var url = url();
        var actor = UUID.randomUUID();
        var writer = UUID.randomUUID();
        prepare(url);
        registerWriter(url, writer, actor, 708, "writer");
        try (var c = DriverManager.getConnection(url)) {
            c.setAutoCommit(false);
            var s = store();
            s.create(c, actor, 708, true, 7, "fp-7", provenance(writer, actor, 708, 1));
            c.commit();
            c.setAutoCommit(false);
            s.delete(c, actor, 708, provenance(writer, actor, 708, 2));
            c.commit();
            c.setAutoCommit(false);
            assertThrows(
                    IllegalStateException.class,
                    () ->
                            s.create(
                                    c,
                                    actor,
                                    708,
                                    true,
                                    7,
                                    "fp-replay",
                                    provenance(writer, actor, 708, 3)));
            c.rollback();
        }
    }

    @Test
    void persistedKeyIdsAreExposedForRetirementCoordination() throws Exception {
        var url = url();
        var actor = UUID.randomUUID();
        var writer = UUID.randomUUID();
        prepare(url);
        registerWriter(url, writer, actor, 709, "writer");
        try (var c = DriverManager.getConnection(url)) {
            c.setAutoCommit(false);
            var s = store();
            s.create(c, actor, 709, true, 1, "fp", provenance(writer, actor, 709, 1));
            var ids = s.getPersistedIntegrityKeyIds(c, true);
            assertTrue(ids.contains("key-v1"));
            c.rollback();
        }
    }

    private static PostgresAuthorizationContextStore.AuthorizationWriteProvenance provenance(
            UUID writer, UUID actor, int tenant, long seq) {
        return new PostgresAuthorizationContextStore.AuthorizationWriteProvenance(
                writer, actor, tenant, seq, "writer");
    }

    private static void prepare(String url) throws Exception {
        try (var c = DriverManager.getConnection(url);
                var statement = c.createStatement()) {
            var schema =
                    new String(
                            PostgresAuthorizationContextStoreParityTest.class
                                    .getResourceAsStream("/high-assurance-postgres-schema.sql")
                                    .readAllBytes(),
                            StandardCharsets.UTF_8);
            for (var sql : schema.split(";\\s*\\n"))
                if (!sql.isBlank() && !sql.trim().startsWith("--")) statement.execute(sql);
            statement.executeUpdate(
                    "TRUNCATE banking.authorization_context_tombstone,"
                        + " banking.authorization_context_writer, banking.authorization_context,"
                        + " banking.transfer_audit, banking.transfer_idempotency,"
                        + " banking.bank_account");
        }
    }

    private static void registerWriter(
            String url, UUID writer, UUID actor, int tenant, String credential) throws Exception {
        try (var c = DriverManager.getConnection(url);
                var p =
                        c.prepareStatement(
                                "INSERT INTO"
                                    + " banking.authorization_context_writer(writer_id,actor_id,tenant_id,active,database_role,credential_fingerprint,last_write_sequence)"
                                    + " VALUES (?,?,?,?,?,?,0)")) {
            p.setObject(1, writer);
            p.setObject(2, actor);
            p.setInt(3, tenant);
            p.setBoolean(4, true);
            try (var role = c.prepareStatement("SELECT current_user");
                    var r = role.executeQuery()) {
                r.next();
                p.setString(5, r.getString(1));
            }
            p.setString(6, credential);
            p.executeUpdate();
        }
    }

    private static void insertRawContext(
            String url,
            UUID actor,
            int tenant,
            boolean allowed,
            long version,
            String fingerprint,
            String keyId,
            String tag)
            throws Exception {
        try (var c = DriverManager.getConnection(url);
                var p =
                        c.prepareStatement(
                                "INSERT INTO"
                                    + " banking.authorization_context(actor_id,tenant_id,allowed,version,fingerprint,integrity_algorithm,integrity_key_id,integrity_tag)"
                                    + " VALUES (?,?,?,?,?,?,?,?)")) {
            p.setObject(1, actor);
            p.setInt(2, tenant);
            p.setBoolean(3, allowed);
            p.setLong(4, version);
            p.setString(5, fingerprint);
            p.setString(6, AuthorizationContextIntegrityKeyRing.currentAlgorithmVersion());
            p.setString(7, keyId);
            p.setString(8, tag);
            p.executeUpdate();
        }
    }
}
