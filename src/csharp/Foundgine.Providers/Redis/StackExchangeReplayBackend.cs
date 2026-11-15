using StackExchange.Redis;

namespace Foundgine.Providers.Redis;

/// <summary>
/// <see cref="IWarrantReplayBackend"/> over StackExchange.Redis. Configure <c>SyncTimeout</c> on the connection so a hung
/// server fails closed instead of blocking requests.
/// </summary>
public sealed class StackExchangeReplayBackend : IWarrantReplayBackend
{
    private readonly IConnectionMultiplexer _connection;
    private readonly int _database;

    public StackExchangeReplayBackend(IConnectionMultiplexer connection, int database = -1)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _database = database;
    }

    public bool SetIfAbsent(string key, TimeSpan ttl)
    {
        // Sends SET key 1 PX ttl NX: true when this call created the key, false when it already existed.
        var expiry = TimeSpan.FromMilliseconds(Math.Max(1d, ttl.TotalMilliseconds));
        return _connection.GetDatabase(_database).StringSet(key, "1", expiry, When.NotExists);
    }
}
