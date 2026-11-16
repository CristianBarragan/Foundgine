using StackExchange.Redis;

namespace Foundgine.Providers.Redis;

/// <summary>
/// <see cref="IRedisScriptBackend"/> over StackExchange.Redis. Configure <c>SyncTimeout</c> on the connection so a hung server
/// fails the request instead of blocking it.
/// </summary>
public sealed class StackExchangeScriptBackend : IRedisScriptBackend
{
    private readonly IConnectionMultiplexer _connection;
    private readonly int _database;

    public StackExchangeScriptBackend(IConnectionMultiplexer connection, int database = -1)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _database = database;
    }

    public IReadOnlyList<string> Eval(string script, IReadOnlyList<string> keys, IReadOnlyList<string> args)
    {
        var result = _connection.GetDatabase(_database).ScriptEvaluate(
            script,
            keys.Select(k => (RedisKey)k).ToArray(),
            args.Select(a => (RedisValue)a).ToArray());

        var items = (RedisResult[]?)result ?? Array.Empty<RedisResult>();
        return items.Select(i => (string?)i ?? string.Empty).ToArray();
    }
}
