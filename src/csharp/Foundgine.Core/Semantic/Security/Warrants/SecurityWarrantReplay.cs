using System.Collections.Concurrent;

namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>Single-use replay protection for a signed warrant nonce.</summary>
public interface ISecurityWarrantReplayStore
{
    bool TryConsume(string warrantId, string nonce);
}

public sealed class MemorySecurityWarrantReplayStore : ISecurityWarrantReplayStore
{
    private readonly ConcurrentDictionary<string, byte> _used = new(StringComparer.Ordinal);

    public bool TryConsume(string warrantId, string nonce) =>
        _used.TryAdd(warrantId + "\u001f" + nonce, 0);
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
        if (!store.TryConsume(warrant.Id, warrant.Nonce))
            throw new InvalidOperationException("Security warrant nonce has already been consumed.");
    }
}