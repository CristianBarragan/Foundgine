namespace Foundgine.Providers.Redis;

/// <summary>
/// The one operation the shared replay store needs from a Redis-protocol server (Redis, Valkey, Microsoft Garnet, which is
/// built on FASTER, or any compatible service).
/// </summary>
public interface IWarrantReplayBackend
{
    /// <summary>
    /// Atomically stores <paramref name="key"/> for <paramref name="ttl"/> only if it does not exist:
    /// <c>SET key 1 NX PX ttl</c>. Returns <c>true</c> if this call created the key and <c>false</c> if it already existed.
    /// An unreachable server or an error reply must throw; the store treats that as a denial, never as success.
    /// </summary>
    bool SetIfAbsent(string key, TimeSpan ttl);
}
