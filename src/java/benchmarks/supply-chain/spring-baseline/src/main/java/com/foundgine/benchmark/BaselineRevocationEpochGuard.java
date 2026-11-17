package com.foundgine.benchmark;

import io.lettuce.core.RedisNoScriptException;
import io.lettuce.core.ScriptOutputType;
import io.lettuce.core.api.sync.RedisCommands;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.Statement;
import java.util.HexFormat;
import java.util.List;
import java.util.UUID;
import javax.sql.DataSource;

/** Plain Spring/JPA baseline implementation of the shared Redis + PostgreSQL revocation epoch gate. */
public final class BaselineRevocationEpochGuard {
    private static final String PREFIX = "benchmark:java:{warrant-revocations}:";
    private static final String SEQUENCE_KEY = PREFIX + "sequence";
    private static final String TABLE = "foundgine_warrant_revocation_epoch";
    // One round trip: sequence, then whether the warrant identity or digest key exists. Same shape as the Foundgine lookup.
    private static final String LOOKUP_SCRIPT = """
            local sequence = redis.call('GET', KEYS[1]) or '0'
            local revoked = '0'
            if redis.call('EXISTS', KEYS[2]) == 1 or redis.call('EXISTS', KEYS[3]) == 1 then revoked = '1' end
            return {tostring(sequence), revoked}
            """;
    private static final String LOOKUP_SHA1 = sha1(LOOKUP_SCRIPT);
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

    /**
     * Same call pattern as the Foundgine path: one durable read, one Redis script (by SHA1, EVAL on NOSCRIPT), and a
     * write only when Redis is ahead of the anchor. This keeps the comparison about the query engine, not the
     * number of revocation round trips.
     */
    public long validate(String warrantId, String tokenDigest) {
        try {
            long durable = readDurable();
            String[] keys = {
                SEQUENCE_KEY,
                PREFIX + "warrant:" + hash(warrantId + "\u001f" + tokenDigest),
                PREFIX + "digest:" + hash(tokenDigest)
            };
            List<?> items = evaluateLookup(keys);
            if (items.size() != 2) throw new IllegalStateException("Redis returned an invalid revocation lookup result");
            String revoked = String.valueOf(items.get(1));
            if (!"0".equals(revoked) && !"1".equals(revoked))
                throw new IllegalStateException("Redis returned an invalid revocation lookup result");
            long anchored = anchor(Long.parseLong(String.valueOf(items.get(0))), durable);
            if ("1".equals(revoked)) throw new SecurityException("Bearer token revoked");
            return anchored;
        } catch (SecurityException | IllegalStateException e) {
            throw e;
        } catch (Exception e) {
            throw new IllegalStateException("Revocation state unavailable; failing closed", e);
        }
    }

    private List<?> evaluateLookup(String[] keys) {
        Object result;
        try {
            result = redis.evalsha(LOOKUP_SHA1, ScriptOutputType.MULTI, keys);
        } catch (RedisNoScriptException noScript) {
            result = redis.eval(LOOKUP_SCRIPT, ScriptOutputType.MULTI, keys);
        }
        if (!(result instanceof List<?> items)) throw new IllegalStateException("Redis returned a non-list script result");
        return items;
    }

    private long readDurable() throws Exception {
        try (Connection c = dataSource.getConnection(); Statement s = c.createStatement();
             ResultSet r = s.executeQuery("SELECT pg_is_in_recovery(), high_water_mark FROM " + TABLE + " WHERE singleton_id=1")) {
            if (!r.next() || r.getBoolean(1)) throw new IllegalStateException("PostgreSQL revocation anchor missing or on standby");
            return r.getLong(2);
        }
    }

    private long synchronize() throws Exception {
        String raw = redis.get(SEQUENCE_KEY);
        return anchor(raw == null ? 0 : Long.parseLong(raw), readDurable());
    }

    private long anchor(long sequence, long durable) throws Exception {
        if (sequence < 0 || sequence < durable)
            throw new IllegalStateException("Redis revocation sequence is below the durable high-water mark");
        // Same steady-state rule as AnchoredSecurityWarrantRevocationStore: nothing to persist when Redis and the anchor agree.
        if (sequence == durable) return sequence;

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

    private static String sha1(String value) {
        try {
            return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-1").digest(value.getBytes(StandardCharsets.UTF_8)));
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-1 is unavailable", e);
        }
    }
}
