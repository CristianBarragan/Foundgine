package com.foundgine.providers.tools.mcp.redis;

import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.StoredSession;
import com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager.Store;
import java.util.List;
import java.util.Objects;

/**
 * Shared MCP session storage for horizontally scaled hosts: pass it to
 * {@link com.foundgine.providers.tools.mcp.FoundgineMcpSessionManager} so that a session created on one replica validates,
 * slides and terminates on every other, and sticky routing is no longer needed for session checks. Works with Redis, Valkey
 * and Microsoft Garnet.
 *
 * <p><b>Atomicity.</b> Each {@link Store} operation is one Lua script, so concurrent replicas never interleave a
 * read-modify-write. {@code touch} keeps the idle deadline monotonic exactly like the in-memory store.
 *
 * <p><b>Layout.</b> One hash per session at {@code prefix + "s:" + digest} (the id itself is never stored; the key is the same
 * SHA-256 digest the manager uses) and one sorted set {@code prefix + "index"} scored by the earlier deadline, which drives
 * {@code removeExpired} and {@code count}. Each hash also carries a server-side expiry of the absolute deadline plus one
 * minute, so an abandoned session cannot outlive its maximum lifetime even if nobody calls {@code purgeExpired}. The default
 * prefix contains a hash tag ({@code {foundgine:mcp}}) so all keys share one Redis Cluster slot, which the multi-key scripts
 * need; keep a tag in any custom prefix. The C# {@code RedisFoundgineMcpSessionStore} uses the same layout, so a mixed fleet
 * can share one server and prefix.
 *
 * <p><b>Failure behaviour.</b> If the server is unreachable or answers with an error, the operation throws
 * {@link IllegalStateException}; the request fails and is never treated as a valid session.
 *
 * <p><b>Known limits.</b> {@code FoundgineMcpSessionManager.create} checks the session cap with a process-local lock, so with
 * several replicas the cap can be exceeded briefly by a few sessions. {@code count()} includes sessions that have expired but
 * were not yet purged, as with the in-memory store. The SSE replay buffer is still per process; see the module docs.
 */
public final class RedisMcpSessionStore implements Store {
    public static final String DEFAULT_KEY_PREFIX = "{foundgine:mcp}:sessions:";

    /** How long a hash outlives its absolute deadline before Redis drops it on its own. */
    static final long RETENTION_GRACE_MILLIS = 60_000L;
    /** 9999-12-31; keeps PEXPIREAT within what Redis accepts when the maximum lifetime is effectively unbounded. */
    static final long MAX_EXPIRE_AT_MILLIS = 253_402_300_799_000L;

    private static final String ADD = """
            if redis.call('EXISTS', KEYS[2]) == 1 then return {'0'} end
            redis.call('HSET', KEYS[2], 'owner', ARGV[2], 'created', ARGV[3], 'accessed', ARGV[4], 'idle', ARGV[5], 'abs', ARGV[6])
            local earlier = ARGV[6]
            if tonumber(ARGV[5]) < tonumber(ARGV[6]) then earlier = ARGV[5] end
            redis.call('ZADD', KEYS[1], earlier, ARGV[1])
            redis.call('PEXPIREAT', KEYS[2], ARGV[7])
            return {'1'}
            """;

    private static final String FIND = """
            local v = redis.call('HMGET', KEYS[1], 'owner', 'created', 'accessed', 'idle', 'abs')
            if not v[5] then return {} end
            return v
            """;

    private static final String TOUCH = """
            local v = redis.call('HMGET', KEYS[2], 'owner', 'created', 'accessed', 'idle', 'abs')
            if not v[5] then return {} end
            if tonumber(ARGV[2]) > tonumber(v[3]) then v[3] = ARGV[2] end
            if tonumber(ARGV[3]) > tonumber(v[4]) then v[4] = ARGV[3] end
            redis.call('HSET', KEYS[2], 'accessed', v[3], 'idle', v[4])
            local earlier = v[5]
            if tonumber(v[4]) < tonumber(v[5]) then earlier = v[4] end
            redis.call('ZADD', KEYS[1], earlier, ARGV[1])
            return v
            """;

    private static final String REMOVE = """
            local n = redis.call('DEL', KEYS[2])
            redis.call('ZREM', KEYS[1], ARGV[1])
            return {tostring(n)}
            """;

    private static final String REMOVE_EXPIRED = """
            local total = 0
            while true do
              local ids = redis.call('ZRANGEBYSCORE', KEYS[1], '-inf', ARGV[1], 'LIMIT', 0, 500)
              if #ids == 0 then break end
              for _, id in ipairs(ids) do redis.call('DEL', ARGV[2] .. id) end
              redis.call('ZREM', KEYS[1], unpack(ids))
              total = total + #ids
            end
            return {tostring(total)}
            """;

    private static final String COUNT = "return {tostring(redis.call('ZCARD', KEYS[1]))}";

    private final RedisScriptBackend backend;
    private final String hashPrefix;
    private final String indexKey;

    public RedisMcpSessionStore(RedisScriptBackend backend) {
        this(backend, DEFAULT_KEY_PREFIX);
    }

    public RedisMcpSessionStore(RedisScriptBackend backend, String keyPrefix) {
        this.backend = Objects.requireNonNull(backend, "backend");
        Objects.requireNonNull(keyPrefix, "keyPrefix");
        if (keyPrefix.isBlank()) throw new IllegalArgumentException("keyPrefix must not be blank.");
        this.hashPrefix = keyPrefix + "s:";
        this.indexKey = keyPrefix + "index";
    }

    @Override
    public boolean tryAdd(StoredSession s) {
        long retainUntil = s.absoluteExpiresAtUnixMilliseconds() >= MAX_EXPIRE_AT_MILLIS - RETENTION_GRACE_MILLIS
                ? MAX_EXPIRE_AT_MILLIS
                : s.absoluteExpiresAtUnixMilliseconds() + RETENTION_GRACE_MILLIS;
        List<String> r = run(ADD, List.of(indexKey, hashKey(s.key())), List.of(
                s.key(), s.ownerDigest() == null ? "" : s.ownerDigest(),
                Long.toString(s.createdAtUnixMilliseconds()), Long.toString(s.lastAccessedAtUnixMilliseconds()),
                Long.toString(s.idleExpiresAtUnixMilliseconds()), Long.toString(s.absoluteExpiresAtUnixMilliseconds()),
                Long.toString(retainUntil)));
        return "1".equals(first(r));
    }

    @Override
    public StoredSession find(String key) {
        return parse(key, run(FIND, List.of(hashKey(key)), List.of()));
    }

    @Override
    public StoredSession touch(String key, long lastAccessedAtUnixMilliseconds, long idleExpiresAtUnixMilliseconds) {
        return parse(key, run(TOUCH, List.of(indexKey, hashKey(key)),
                List.of(key, Long.toString(lastAccessedAtUnixMilliseconds), Long.toString(idleExpiresAtUnixMilliseconds))));
    }

    @Override
    public boolean remove(String key) {
        return "1".equals(first(run(REMOVE, List.of(indexKey, hashKey(key)), List.of(key))));
    }

    @Override
    public int removeExpired(long nowUnixMilliseconds) {
        return Integer.parseInt(first(run(REMOVE_EXPIRED, List.of(indexKey),
                List.of(Long.toString(nowUnixMilliseconds), hashPrefix))));
    }

    @Override
    public int count() {
        return Integer.parseInt(first(run(COUNT, List.of(indexKey), List.of())));
    }

    private String hashKey(String key) {
        return hashPrefix + key;
    }

    private List<String> run(String script, List<String> keys, List<String> args) {
        try {
            return backend.eval(script, keys, args);
        } catch (RuntimeException e) {
            throw new IllegalStateException("MCP session store unavailable.", e);
        }
    }

    private static String first(List<String> reply) {
        if (reply == null || reply.isEmpty()) throw new IllegalStateException("MCP session store returned no reply.");
        return reply.get(0);
    }

    /** An empty reply means the session does not exist; otherwise owner, created, accessed, idle, absolute. */
    private static StoredSession parse(String key, List<String> r) {
        if (r == null || r.isEmpty()) return null;
        if (r.size() != 5) throw new IllegalStateException("MCP session store returned a malformed session.");
        try {
            String owner = r.get(0);
            return new StoredSession(key, owner.isEmpty() ? null : owner, Long.parseLong(r.get(1)),
                    Long.parseLong(r.get(2)), Long.parseLong(r.get(3)), Long.parseLong(r.get(4)));
        } catch (NumberFormatException e) {
            throw new IllegalStateException("MCP session store returned a malformed session.", e);
        }
    }
}
