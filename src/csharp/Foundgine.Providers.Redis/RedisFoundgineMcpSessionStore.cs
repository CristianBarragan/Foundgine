using System.Globalization;
using Foundgine.Providers.Tools.MCP;

namespace Foundgine.Providers.Redis;

/// <summary>
/// Shared MCP session storage for horizontally scaled hosts: pass it to <see cref="FoundgineMcpSessionManager"/> so that a
/// session created on one replica validates, slides and terminates on every other, and sticky routing is no longer needed for
/// session checks. Works with Redis, Valkey and Microsoft Garnet.
/// </summary>
/// <remarks>
/// <para><b>Atomicity.</b> Each store operation is one Lua script, so concurrent replicas never interleave a read-modify-write.
/// <c>Touch</c> keeps the idle deadline monotonic exactly like the in-memory store.</para>
/// <para><b>Layout.</b> One hash per session at <c>prefix + "s:" + digest</c> (the id itself is never stored; the key is the same
/// SHA-256 digest the manager uses) and one sorted set <c>prefix + "index"</c> scored by the earlier deadline, which drives
/// <c>RemoveExpired</c> and <c>Count</c>. Each hash also carries a server-side expiry of the absolute deadline plus one minute,
/// so an abandoned session cannot outlive its maximum lifetime even if nobody calls <c>PurgeExpired</c>. The default prefix
/// contains a hash tag (<c>{foundgine:mcp}</c>) so all keys share one Redis Cluster slot, which the multi-key scripts need; keep a
/// tag in any custom prefix. The Java <c>RedisMcpSessionStore</c> uses the same layout, so a mixed fleet can share one server and
/// prefix.</para>
/// <para><b>Failure behaviour.</b> If the server is unreachable or answers with an error, the operation throws
/// <see cref="InvalidOperationException"/>; the request fails and is never treated as a valid session.</para>
/// <para><b>Known limits.</b> <see cref="FoundgineMcpSessionManager.Create"/> checks the session cap with a process-local lock, so with
/// several replicas the cap can be exceeded briefly by a few sessions. <see cref="Count"/> includes sessions that have expired but
/// were not yet purged, as with the in-memory store. The SSE replay buffer is still per process; see the module docs.</para>
/// </remarks>
public sealed class RedisFoundgineMcpSessionStore : IFoundgineMcpSessionStore
{
    public const string DefaultKeyPrefix = "{foundgine:mcp}:sessions:";

    /// <summary>How long a hash outlives its absolute deadline before Redis drops it on its own.</summary>
    internal const long RetentionGraceMilliseconds = 60_000;

    /// <summary>9999-12-31; keeps PEXPIREAT within what Redis accepts when the maximum lifetime is effectively unbounded.</summary>
    internal const long MaxExpireAtMilliseconds = 253_402_300_799_000;

    private const string AddScript = """
        if redis.call('EXISTS', KEYS[2]) == 1 then return {'0'} end
        redis.call('HSET', KEYS[2], 'owner', ARGV[2], 'created', ARGV[3], 'accessed', ARGV[4], 'idle', ARGV[5], 'abs', ARGV[6])
        local earlier = ARGV[6]
        if tonumber(ARGV[5]) < tonumber(ARGV[6]) then earlier = ARGV[5] end
        redis.call('ZADD', KEYS[1], earlier, ARGV[1])
        redis.call('PEXPIREAT', KEYS[2], ARGV[7])
        return {'1'}
        """;

    private const string FindScript = """
        local v = redis.call('HMGET', KEYS[1], 'owner', 'created', 'accessed', 'idle', 'abs')
        if not v[5] then return {} end
        return v
        """;

    private const string TouchScript = """
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

    private const string RemoveScript = """
        local n = redis.call('DEL', KEYS[2])
        redis.call('ZREM', KEYS[1], ARGV[1])
        return {tostring(n)}
        """;

    private const string RemoveExpiredScript = """
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

    private const string CountScript = "return {tostring(redis.call('ZCARD', KEYS[1]))}";

    private readonly IRedisScriptBackend _backend;
    private readonly string _hashPrefix;
    private readonly string _indexKey;

    public RedisFoundgineMcpSessionStore(IRedisScriptBackend backend, string keyPrefix = DefaultKeyPrefix)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        _hashPrefix = keyPrefix + "s:";
        _indexKey = keyPrefix + "index";
    }

    public bool TryAdd(FoundgineMcpStoredSession session)
    {
        var abs = session.AbsoluteExpiresAtUnixMilliseconds;
        var retainUntil = abs >= MaxExpireAtMilliseconds - RetentionGraceMilliseconds
            ? MaxExpireAtMilliseconds
            : abs + RetentionGraceMilliseconds;

        var reply = Run(AddScript, [_indexKey, HashKey(session.Key)],
        [
            session.Key,
            session.OwnerDigest ?? "",
            L(session.CreatedAtUnixMilliseconds),
            L(session.LastAccessedAtUnixMilliseconds),
            L(session.IdleExpiresAtUnixMilliseconds),
            L(abs),
            L(retainUntil),
        ]);
        return First(reply) == "1";
    }

    public FoundgineMcpStoredSession? Find(string key) =>
        Parse(key, Run(FindScript, [HashKey(key)], []));

    public FoundgineMcpStoredSession? Touch(string key, long lastAccessedUnixMilliseconds, long idleExpiresAtUnixMilliseconds) =>
        Parse(key, Run(TouchScript, [_indexKey, HashKey(key)],
            [key, L(lastAccessedUnixMilliseconds), L(idleExpiresAtUnixMilliseconds)]));

    public bool Remove(string key) =>
        First(Run(RemoveScript, [_indexKey, HashKey(key)], [key])) == "1";

    public int RemoveExpired(long nowUnixMilliseconds) =>
        int.Parse(First(Run(RemoveExpiredScript, [_indexKey], [L(nowUnixMilliseconds), _hashPrefix])), CultureInfo.InvariantCulture);

    public int Count => int.Parse(First(Run(CountScript, [_indexKey], [])), CultureInfo.InvariantCulture);

    private string HashKey(string key) => _hashPrefix + key;

    private static string L(long value) => value.ToString(CultureInfo.InvariantCulture);

    private IReadOnlyList<string> Run(string script, IReadOnlyList<string> keys, IReadOnlyList<string> args)
    {
        try
        {
            return _backend.Eval(script, keys, args);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("MCP session store unavailable.", ex);
        }
    }

    private static string First(IReadOnlyList<string> reply) =>
        reply is { Count: > 0 } ? reply[0] : throw new InvalidOperationException("MCP session store returned no reply.");

    /// <summary>An empty reply means the session does not exist; otherwise owner, created, accessed, idle, absolute.</summary>
    private static FoundgineMcpStoredSession? Parse(string key, IReadOnlyList<string> reply)
    {
        if (reply.Count == 0) return null;
        if (reply.Count != 5) throw new InvalidOperationException("MCP session store returned a malformed session.");

        if (!long.TryParse(reply[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var created)
            || !long.TryParse(reply[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var accessed)
            || !long.TryParse(reply[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idle)
            || !long.TryParse(reply[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var absolute))
            throw new InvalidOperationException("MCP session store returned a malformed session.");

        return new FoundgineMcpStoredSession(key, reply[0].Length == 0 ? null : reply[0], created, accessed, idle, absolute);
    }
}
