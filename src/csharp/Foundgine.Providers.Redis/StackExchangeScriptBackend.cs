using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace Foundgine.Providers.Redis;

/// <summary>
/// <see cref="IRedisScriptBackend"/> over StackExchange.Redis. Configure <c>SyncTimeout</c> on the connection so a hung server
/// fails the request instead of blocking it.
/// </summary>
/// <remarks>
/// Scripts are invoked by SHA1 (EVALSHA) so the script text is not re-sent on every call. If the server does not have the
/// script cached (first use, restart, SCRIPT FLUSH, failover), the call falls back to EVAL, which also caches it.
/// </remarks>
public sealed class StackExchangeScriptBackend : IRedisScriptBackend
{
    private static readonly ConcurrentDictionary<string, byte[]> ScriptHashes = new(StringComparer.Ordinal);

    private readonly IConnectionMultiplexer _connection;
    private readonly int _database;

    public StackExchangeScriptBackend(IConnectionMultiplexer connection, int database = -1)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _database = database;
    }

    public IReadOnlyList<string> Eval(string script, IReadOnlyList<string> keys, IReadOnlyList<string> args)
    {
        var database = _connection.GetDatabase(_database);
        var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
        var redisArgs = args.Select(a => (RedisValue)a).ToArray();
        var hash = ScriptHashes.GetOrAdd(script, static s => SHA1.HashData(Encoding.UTF8.GetBytes(s)));

        RedisResult result;
        try
        {
            result = database.ScriptEvaluate(hash, redisKeys, redisArgs);
        }
        catch (RedisServerException ex) when (ex.Message.StartsWith("NOSCRIPT", StringComparison.OrdinalIgnoreCase))
        {
            result = database.ScriptEvaluate(script, redisKeys, redisArgs);
        }

        var items = (RedisResult[]?)result ?? Array.Empty<RedisResult>();
        return items.Select(i => (string?)i ?? string.Empty).ToArray();
    }
}
