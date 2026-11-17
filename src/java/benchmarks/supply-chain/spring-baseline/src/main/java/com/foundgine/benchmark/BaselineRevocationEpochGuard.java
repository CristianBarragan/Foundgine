package com.foundgine.benchmark;

import io.lettuce.core.api.sync.RedisCommands;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.Statement;
import java.util.HexFormat;
import java.util.UUID;
import javax.sql.DataSource;

/** Plain Spring/JPA baseline implementation of the shared Redis + PostgreSQL revocation epoch gate. */
public final class BaselineRevocationEpochGuard {
    private static final String PREFIX = "benchmark:java:{warrant-revocations}:";
    private static final String SEQUENCE_KEY = PREFIX + "sequence";
    private static final String TABLE = "foundgine_warrant_revocation_epoch";
    private final RedisCommands<String, String> redis;
    private final DataSource dataSource;

    public BaselineRevocationEpochGuard(RedisCommands<String, String> redis, DataSource dataSource) {
        this.redis = redis;
        this.dataSource = dataSource;
    }

    public void migrate() {
        try (Connection c = dataSource.getConnection(); Statement s = c.createStatement()) {
            s.execute("CREATE TABLE IF NOT EXISTS " + TABLE + " (singleton_id smallint PRIMARY KEY CHECK(singleton_id=1), high_water_mark bigint NOT NULL CHECK(high_water_mark>=0))");
            s.execute("INSERT INTO " + TABLE + " (singleton_id, high_water_mark) VALUES (1,0) ON CONFLICT(singleton_id) DO NOTHING");
        } catch (Exception e) {
            throw new IllegalStateException("Could not initialize baseline revocation epoch anchor", e);
        }
    }

    /** Mirrors the Foundgine guard's identity lookup, digest lookup and sequence bracketing. */
    public long validate(String warrantId, String tokenDigest) {
        try {
            long before = synchronize();
            String identityKey = PREFIX + "warrant:" + hash(warrantId + "\u001f" + tokenDigest);
            if (redis.exists(identityKey) > 0) throw new SecurityException("Bearer token revoked");
            long afterIdentity = synchronize();
            String digestKey = PREFIX + "digest:" + hash(tokenDigest);
            if (redis.exists(digestKey) > 0) throw new SecurityException("Bearer token revoked");
            long afterDigest = synchronize();
            long after = synchronize();
            if (before != afterIdentity || before != afterDigest || before != after)
                throw new IllegalStateException("Revocation state changed during validation");
            return after;
        } catch (SecurityException | IllegalStateException e) {
            throw e;
        } catch (Exception e) {
            throw new IllegalStateException("Revocation state unavailable; failing closed", e);
        }
    }

    private long synchronize() throws Exception {
        long durable;
        try (Connection c = dataSource.getConnection(); Statement s = c.createStatement();
             ResultSet r = s.executeQuery("SELECT pg_is_in_recovery(), high_water_mark FROM " + TABLE + " WHERE singleton_id=1")) {
            if (!r.next() || r.getBoolean(1)) throw new IllegalStateException("PostgreSQL revocation anchor missing or on standby");
            durable = r.getLong(2);
        }

        String raw = redis.get(SEQUENCE_KEY);
        long sequence = raw == null ? 0 : Long.parseLong(raw);
        if (sequence < 0 || sequence < durable)
            throw new IllegalStateException("Redis revocation sequence is below the durable high-water mark");

        try (Connection c = dataSource.getConnection()) {
            c.setAutoCommit(false);
            try (Statement durability = c.createStatement()) {
                durability.execute("SET LOCAL synchronous_commit=on");
            }
            try (PreparedStatement p = c.prepareStatement("INSERT INTO " + TABLE
                    + " (singleton_id,high_water_mark) VALUES (1,?) ON CONFLICT(singleton_id) DO UPDATE SET high_water_mark=GREATEST("
                    + TABLE + ".high_water_mark,EXCLUDED.high_water_mark)")) {
                p.setLong(1, sequence);
                p.executeUpdate();
            }
            c.commit();
        }

        try (Connection c = dataSource.getConnection(); Statement s = c.createStatement();
             ResultSet r = s.executeQuery("SELECT pg_is_in_recovery(), high_water_mark FROM " + TABLE + " WHERE singleton_id=1")) {
            if (!r.next() || r.getBoolean(1) || r.getLong(2) != sequence)
                throw new IllegalStateException("PostgreSQL revocation anchor read-after-write failed");
        }
        return sequence;
    }

    public long probe() {
        String key = SEQUENCE_KEY + ":health:" + UUID.randomUUID().toString().replace("-", "");
        String token = UUID.randomUUID().toString();
        if (!"OK".equals(redis.setex(key, 5, token)) || !token.equals(redis.get(key)))
            throw new IllegalStateException("Redis readiness probe failed");
        redis.del(key);
        try {
            return synchronize();
        } catch (Exception e) {
            throw new IllegalStateException("Revocation readiness failed", e);
        }
    }

    private static String hash(String value) {
        try {
            return HexFormat.of().withUpperCase().formatHex(
                    MessageDigest.getInstance("SHA-256").digest(value.getBytes(StandardCharsets.UTF_8)));
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-256 is unavailable", e);
        }
    }
}
