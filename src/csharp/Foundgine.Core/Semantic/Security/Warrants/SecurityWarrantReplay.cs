using System.Collections.Concurrent;

namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>
/// Single-use replay protection for a signed warrant nonce. Implementations must be atomic: for one
/// (warrantId, nonce) pair exactly one caller, on any host sharing the store, may receive <c>true</c>.
/// </summary>
public interface ISecurityWarrantReplayStore
{
    /// <summary>Records the pair with no known lifetime. Stores cannot expire such entries.</summary>
    bool TryConsume(string warrantId, string nonce);

    /// <summary>
    /// Records the pair until the warrant expires so the store can discard it afterwards. An expired warrant is rejected
    /// by the time check before it reaches the store, so retaining an entry past <paramref name="expiresAt"/> (plus clock
    /// skew) has no security value. The default ignores the expiry and delegates, so existing custom stores keep
    /// working; override it to bound storage (for example a Redis TTL).
    /// </summary>
    bool TryConsume(string warrantId, string nonce, DateTimeOffset expiresAt) => TryConsume(warrantId, nonce);
}

/// <summary>
/// Per-process replay store. Entries consumed with an expiry are discarded after the warrant expires plus a small grace
/// for clock skew, so the store stays bounded by the warrants issued within one lifetime. Entries consumed without an
/// expiry are kept for the life of the process.
/// </summary>
public sealed class MemorySecurityWarrantReplayStore : ISecurityWarrantReplayStore
{
    /// <summary>Extra retention after expiry, covering clock skew between the store and the caller that checked the time.</summary>
    internal static readonly TimeSpan RetentionGrace = TimeSpan.FromMinutes(1);

    private const int PurgeInterval = 256;

    private readonly ConcurrentDictionary<string, long> _used = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private int _sincePurge;

    public MemorySecurityWarrantReplayStore() : this(TimeProvider.System)
    {
    }

    public MemorySecurityWarrantReplayStore(TimeProvider timeProvider)
    {
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>Number of retained entries (diagnostics and tests).</summary>
    public int Count => _used.Count;

    public bool TryConsume(string warrantId, string nonce) =>
        Consume(warrantId, nonce, long.MaxValue);

    public bool TryConsume(string warrantId, string nonce, DateTimeOffset expiresAt) =>
        Consume(warrantId, nonce, RetainUntil(expiresAt));

    private bool Consume(string warrantId, string nonce, long retainUntilMilliseconds)
    {
        PurgeIfDue();
        return _used.TryAdd(warrantId + "\u001f" + nonce, retainUntilMilliseconds);
    }

    private void PurgeIfDue()
    {
        if (Interlocked.Increment(ref _sincePurge) < PurgeInterval)
            return;

        Interlocked.Exchange(ref _sincePurge, 0);
        var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        foreach (var entry in _used)
        {
            if (entry.Value <= now)
                _used.TryRemove(entry);
        }
    }

    internal static long RetainUntil(DateTimeOffset expiresAt)
    {
        var expiry = expiresAt.ToUnixTimeMilliseconds();
        var grace = (long)RetentionGrace.TotalMilliseconds;
        return expiry > long.MaxValue - grace ? long.MaxValue : expiry + grace;
    }
}

public static class SecurityWarrantReplayGuard
{
    public static void Consume(
        SecurityWarrant warrant,
        ISecurityWarrantReplayStore store,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        ArgumentNullException.ThrowIfNull(store);
        if (!warrant.IsTimeValid(now))
            throw new InvalidOperationException("Security warrant is expired or not yet valid.");
        if (!store.TryConsume(warrant.Id, warrant.Nonce, warrant.ExpiresAt))
            throw new InvalidOperationException("Security warrant nonce has already been consumed.");
    }
}
