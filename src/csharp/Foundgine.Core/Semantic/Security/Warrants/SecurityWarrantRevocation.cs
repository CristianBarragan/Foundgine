using System.Collections.Concurrent;

namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>Records a revoked warrant digest. Revocation is monotonic and cannot be undone.</summary>
public sealed record SecurityWarrantRevocation(
    string WarrantId,
    string WarrantDigest,
    DateTimeOffset RevokedAt,
    long Sequence);

/// <summary>Monotonic revocation authority used at execution time.</summary>
public interface ISecurityWarrantRevocationStore
{
    long CurrentSequence { get; }
    SecurityWarrantRevocation Revoke(SecurityWarrant warrant, DateTimeOffset now);
    bool IsRevoked(string warrantId, string warrantDigest);
    bool IsDigestRevoked(string warrantDigest);
}

/// <summary>Thread-safe in-memory implementation for security tests and deterministic execution tests.</summary>
public sealed class MemorySecurityWarrantRevocationStore : ISecurityWarrantRevocationStore
{
    private readonly ConcurrentDictionary<string, SecurityWarrantRevocation> _revoked = new(StringComparer.Ordinal);
    private long _sequence;

    public long CurrentSequence => Interlocked.Read(ref _sequence);

    public SecurityWarrantRevocation Revoke(SecurityWarrant warrant, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        if (string.IsNullOrWhiteSpace(warrant.Id) || string.IsNullOrWhiteSpace(warrant.Digest))
            throw new InvalidOperationException("A warrant must have an identity and digest before revocation.");

        var key = warrant.Id + "\u001f" + warrant.Digest;
        var sequence = Interlocked.Increment(ref _sequence);
        var entry = new SecurityWarrantRevocation(warrant.Id, warrant.Digest, now, sequence);
        _revoked.TryAdd(key, entry);
        return _revoked[key];
    }

    public bool IsRevoked(string warrantId, string warrantDigest) =>
        _revoked.ContainsKey(warrantId + "\u001f" + warrantDigest);

    public bool IsDigestRevoked(string warrantDigest) =>
        _revoked.Values.Any(x => StringComparer.Ordinal.Equals(x.WarrantDigest, warrantDigest));
}

/// <summary>Immutable execution-time view of revocation state.</summary>
public readonly record struct SecurityWarrantRevocationSnapshot(long Sequence)
{
    public static SecurityWarrantRevocationSnapshot Capture(ISecurityWarrantRevocationStore store) =>
        new(store.CurrentSequence);

    public void AssertUnchanged(ISecurityWarrantRevocationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store.CurrentSequence != Sequence)
            throw new InvalidOperationException("Authorization revocation state changed during execution.");
    }
}

/// <summary>
/// Rejects a warrant when the warrant itself or any delegated ancestor has been revoked.
/// A child cannot outlive the authority from which it was delegated.
/// </summary>
public static class SecurityWarrantRevocationGuard
{
    public static SecurityWarrantRevocationSnapshot Validate(
        SecurityWarrant warrant,
        ISecurityWarrantRevocationStore store,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        ArgumentNullException.ThrowIfNull(store);
        if (!warrant.IsTimeValid(now))
            throw new InvalidOperationException("Security warrant is expired or not yet valid.");

        if (store.IsRevoked(warrant.Id, warrant.Digest) || store.IsDigestRevoked(warrant.Digest))
            throw new InvalidOperationException("Security warrant has been revoked.");

        foreach (var ancestorDigest in warrant.DelegationPath)
        {
            if (store.IsDigestRevoked(ancestorDigest))
                throw new InvalidOperationException("A parent security warrant has been revoked.");
        }

        return SecurityWarrantRevocationSnapshot.Capture(store);
    }
}
