using System.Security.Cryptography;
using System.Text;
using Foundgine.Core.Semantic.Security.Warrants;

namespace Foundgine.Providers.Redis;

/// <summary>
/// Shared warrant replay protection for horizontally scaled hosts. Each consumed warrant is recorded with one atomic
/// <c>SET key 1 NX PX ttl</c>: the first host to consume a warrant wins and every other attempt, on any host, is rejected.
/// Works with Redis, Valkey and Microsoft Garnet (the Redis-protocol server built on FASTER). FASTER itself is an
/// embedded single-process store and cannot share state between replicas; Garnet is the networked option.
/// </summary>
/// <remarks>
/// <para><b>Failure behaviour: fail closed.</b> If the server is unreachable or errors, <c>TryConsume</c> throws
/// <see cref="InvalidOperationException"/> and never returns <c>true</c>, so the request is denied rather than allowed
/// without replay protection. Run the server with replication if an outage must not stop traffic.</para>
/// <para><b>Retention.</b> With an expiry, an entry lives until the warrant expires plus <see cref="DefaultGrace"/> for
/// clock skew. Without one (the two-argument overload) it lives for the default retention; set that to at least the
/// longest warrant lifetime you issue.</para>
/// <para><b>Keys</b> are <c>prefix + SHA-256(warrantId 0x1F nonce)</c> in upper-case hex, the same identity the file store
/// uses, so no raw nonce is stored. Use one prefix per environment when sharing a server.</para>
/// </remarks>
public sealed class RedisSecurityWarrantReplayStore : ISecurityWarrantReplayStore
{
    public const string DefaultKeyPrefix = "foundgine:warrant-replay:";
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(1);

    // Redis rejects a PX that overflows; ten years is far beyond any real warrant lifetime.
    private static readonly TimeSpan MaxTtl = TimeSpan.FromDays(3650);

    private readonly IWarrantReplayBackend _backend;
    private readonly string _keyPrefix;
    private readonly TimeSpan _defaultRetention;
    private readonly TimeSpan _grace;
    private readonly TimeProvider _time;

    public RedisSecurityWarrantReplayStore(
        IWarrantReplayBackend backend,
        string keyPrefix = DefaultKeyPrefix,
        TimeSpan? defaultRetention = null,
        TimeSpan? grace = null,
        TimeProvider? timeProvider = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _keyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
        _defaultRetention = RequirePositive(defaultRetention ?? DefaultRetention, nameof(defaultRetention));
        _grace = RequirePositive(grace ?? DefaultGrace, nameof(grace));
        _time = timeProvider ?? TimeProvider.System;
    }

    public bool TryConsume(string warrantId, string nonce) =>
        Consume(warrantId, nonce, _defaultRetention);

    public bool TryConsume(string warrantId, string nonce, DateTimeOffset expiresAt) =>
        Consume(warrantId, nonce, TtlFor(expiresAt));

    private TimeSpan TtlFor(DateTimeOffset expiresAt)
    {
        var remaining = expiresAt - _time.GetUtcNow();

        // Already expired by this host's clock: the time check rejects it, but keep a short entry for the skew window.
        var ttl = remaining <= TimeSpan.Zero ? _grace : remaining + _grace;
        return ttl > MaxTtl ? MaxTtl : ttl;
    }

    private bool Consume(string warrantId, string nonce, TimeSpan ttl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warrantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        try
        {
            return _backend.SetIfAbsent(_keyPrefix + Identity(warrantId, nonce), ttl);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Warrant replay store unavailable; failing closed.", ex);
        }
    }

    private static string Identity(string warrantId, string nonce) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(warrantId + "\u001f" + nonce)));

    private static TimeSpan RequirePositive(TimeSpan value, string name) =>
        value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(name, "Must be positive.");
}
