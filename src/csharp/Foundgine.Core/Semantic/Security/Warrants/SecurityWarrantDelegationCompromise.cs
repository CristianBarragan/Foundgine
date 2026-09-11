using System.Collections.Concurrent;

namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>
/// Records a security compromise rooted at an exact warrant subtree.  Compromise is
/// monotonic: once an authority node is marked compromised it cannot be restored.
/// </summary>
public sealed record SecurityWarrantDelegationCompromise(
    string RootWarrantId,
    string RootWarrantDigest,
    string? CompromisedIssuer,
    string? CompromisedKeyId,
    DateTimeOffset CompromisedAt,
    long Sequence);

/// <summary>Execution-time authority for invalidating an affected delegation subtree.</summary>
public interface ISecurityWarrantDelegationCompromiseStore
{
    long CurrentSequence { get; }

    SecurityWarrantDelegationCompromise Compromise(
        SecurityWarrant root,
        DateTimeOffset now,
        string? compromisedIssuer = null,
        string? compromisedKeyId = null);

    bool IsCompromised(SecurityWarrant warrant);

    bool IsCompromisedByAncestor(SecurityWarrant warrant);

    bool IsCompromisedByIssuerOrKey(SecurityWarrant warrant);
}

/// <summary>Deterministic in-memory implementation used by adversarial security tests.</summary>
public sealed class MemorySecurityWarrantDelegationCompromiseStore
    : ISecurityWarrantDelegationCompromiseStore
{
    private readonly ConcurrentDictionary<string, SecurityWarrantDelegationCompromise> _compromises =
        new(StringComparer.Ordinal);

    private long _sequence;

    public long CurrentSequence => Interlocked.Read(ref _sequence);

    public SecurityWarrantDelegationCompromise Compromise(
        SecurityWarrant root,
        DateTimeOffset now,
        string? compromisedIssuer = null,
        string? compromisedKeyId = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (string.IsNullOrWhiteSpace(root.Id) || string.IsNullOrWhiteSpace(root.Digest))
            throw new InvalidOperationException(
                "A warrant must have an identity and digest before compromise registration.");
        if (compromisedIssuer is null && compromisedKeyId is null)
            throw new ArgumentException("A compromised issuer or key must be supplied.");

        var key = root.Id + "\u001f" + root.Digest;
        var sequence = Interlocked.Increment(ref _sequence);
        var entry = new SecurityWarrantDelegationCompromise(
            root.Id,
            root.Digest,
            compromisedIssuer,
            compromisedKeyId,
            now,
            sequence);
        _compromises.TryAdd(key, entry);
        return _compromises[key];
    }

    public bool IsCompromised(SecurityWarrant warrant)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        return _compromises.ContainsKey(Key(warrant.Id, warrant.Digest));
    }

    public bool IsCompromisedByAncestor(SecurityWarrant warrant)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        foreach (var ancestorDigest in warrant.DelegationPath)
        {
            if (_compromises.Values.Any(x => StringComparer.Ordinal.Equals(x.RootWarrantDigest, ancestorDigest)))
                return true;
        }

        return false;
    }

    public bool IsCompromisedByIssuerOrKey(SecurityWarrant warrant)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        foreach (var compromise in _compromises.Values)
        {
            if (!StringComparer.Ordinal.Equals(compromise.RootWarrantDigest, warrant.Digest) &&
                !warrant.DelegationPath.Contains(compromise.RootWarrantDigest, StringComparer.Ordinal))
                continue;

            if (compromise.CompromisedIssuer is not null &&
                StringComparer.Ordinal.Equals(compromise.CompromisedIssuer, warrant.Issuer))
                return true;
            if (compromise.CompromisedKeyId is not null &&
                StringComparer.Ordinal.Equals(compromise.CompromisedKeyId, warrant.KeyId))
                return true;
        }

        return false;
    }

    private static string Key(string id, string digest) => id + "\u001f" + digest;
}

/// <summary>
/// Validates that a warrant remains inside an uncompromised delegation subtree.
/// Compromise is intentionally path-bound: compromising one delegation node does
/// not invalidate an unrelated sibling branch.
/// </summary>
public static class SecurityWarrantDelegationCompromiseGuard
{
    public static void Validate(
        SecurityWarrant warrant,
        ISecurityWarrantDelegationCompromiseStore store,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        ArgumentNullException.ThrowIfNull(store);
        if (!warrant.IsTimeValid(now))
            throw new InvalidOperationException("Security warrant is expired or not yet valid.");

        if (store.IsCompromised(warrant) ||
            store.IsCompromisedByAncestor(warrant) ||
            store.IsCompromisedByIssuerOrKey(warrant))
            throw new InvalidOperationException("Security warrant belongs to a compromised delegation subtree.");
    }

    public static SecurityWarrantRevocationSnapshot Capture(
        ISecurityWarrantDelegationCompromiseStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new SecurityWarrantRevocationSnapshot(store.CurrentSequence);
    }

    public static void AssertUnchanged(
        ISecurityWarrantDelegationCompromiseStore store,
        SecurityWarrantRevocationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store.CurrentSequence != snapshot.Sequence)
            throw new InvalidOperationException("Delegation compromise state changed during execution.");
    }
}